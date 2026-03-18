"""The Lucia Home Agent integration."""
from __future__ import annotations

import logging

import httpx

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ConfigEntryNotReady
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers.typing import ConfigType

from .const import (
    CONF_API_KEY,
    CONF_REPOSITORY,
    CONF_VERIFY_SSL,
    DOMAIN,
)

_LOGGER = logging.getLogger(__name__)

PLATFORMS: list[Platform] = [Platform.CONVERSATION]

CONFIG_SCHEMA = cv.config_entry_only_config_schema(DOMAIN)


async def async_setup(hass: HomeAssistant, config: ConfigType) -> bool:
    """Set up the Lucia component."""
    hass.data.setdefault(DOMAIN, {})
    return True


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Set up Lucia from a config entry."""
    hass.data.setdefault(DOMAIN, {})

    repository = entry.data.get(CONF_REPOSITORY)
    api_key = entry.data.get(CONF_API_KEY)
    verify_ssl = entry.data.get(CONF_VERIFY_SSL, False)

    if not repository:
        _LOGGER.error("Missing required configuration: repository URL")
        return False

    repository = repository.rstrip("/")

    try:
        headers = {}
        if api_key:
            headers["X-Api-Key"] = api_key

        httpx_client = httpx.AsyncClient(
            headers=headers,
            verify=verify_ssl,
            follow_redirects=True,
            timeout=30.0,
        )

        # Lightweight health check — just verify the server is reachable
        health_url = f"{repository}/api/conversation"
        try:
            probe = await httpx_client.options(health_url, timeout=10.0)
            _LOGGER.info(
                "Lucia conversation endpoint probe returned HTTP %s",
                probe.status_code,
            )
        except httpx.ConnectError as err:
            await httpx_client.aclose()
            raise ConfigEntryNotReady(
                f"Cannot connect to Lucia at {repository}: {err}"
            ) from err
        except Exception as err:
            # Non-fatal — OPTIONS may not be implemented, that's fine
            _LOGGER.debug(
                "Health probe to %s failed (%s) — continuing anyway", health_url, err
            )

        hass.data[DOMAIN][entry.entry_id] = {
            "httpx_client": httpx_client,
            "repository": repository,
        }

        # Best-effort connectivity validation (dashboard green checkmark)
        await _validate_lucia_connection(hass, httpx_client, repository)

    except ConfigEntryNotReady:
        raise
    except Exception as err:
        _LOGGER.error("Failed to set up Lucia integration: %s", err)
        raise ConfigEntryNotReady(f"Error connecting to Lucia: {err}") from err

    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)

    await async_register_services(hass, entry)
    await async_setup_websocket_api(hass, entry)

    entry.async_on_unload(entry.add_update_listener(async_reload_entry))

    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Unload a config entry."""
    unload_ok = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)

    if unload_ok:
        entry_data = hass.data[DOMAIN].get(entry.entry_id)
        if entry_data and "httpx_client" in entry_data:
            await entry_data["httpx_client"].aclose()

        hass.data[DOMAIN].pop(entry.entry_id, None)

        if not hass.data[DOMAIN]:
            await async_unregister_services(hass)

    return unload_ok


async def _validate_lucia_connection(
    hass: HomeAssistant, client: httpx.AsyncClient, repository: str
) -> None:
    """Call Lucia's validate-ha-connection endpoint to confirm plugin connectivity.

    This is best-effort — if validation fails the plugin still functions normally.
    Success causes the Lucia dashboard setup wizard to show a green checkmark.
    """
    try:
        from homeassistant.helpers.instance_id import async_get as async_get_instance_id

        instance_id = await async_get_instance_id(hass)
        validate_url = f"{repository}/api/setup/validate-ha-connection"
        response = await client.post(
            validate_url,
            json={"homeAssistantInstanceId": instance_id},
            timeout=10.0,
        )
        if response.status_code == 200:
            _LOGGER.info("Lucia connectivity validation succeeded")
        else:
            _LOGGER.warning(
                "Lucia connectivity validation returned HTTP %s — "
                "dashboard may not show plugin as connected",
                response.status_code,
            )
    except Exception as err:
        _LOGGER.warning(
            "Could not validate Lucia connectivity (%s) — "
            "this is non-fatal, plugin will still function",
            err,
        )


async def async_register_services(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """Register Lucia services."""

    async def handle_send_message(call):
        """Handle the send_message service."""
        message = call.data.get("message")

        client_data = hass.data[DOMAIN].get(entry.entry_id)
        if not client_data:
            _LOGGER.error("No client available for service call")
            return

        from .fast_conversation import send_conversation

        httpx_client = client_data["httpx_client"]
        repository = client_data["repository"]

        result = await send_conversation(
            client=httpx_client,
            base_url=repository,
            text=message,
        )
        _LOGGER.debug("Service send_message result: %s", result.text[:100])
        return result.text

    if not hass.services.has_service(DOMAIN, "send_message"):
        hass.services.async_register(
            DOMAIN,
            "send_message",
            handle_send_message,
        )


async def async_unregister_services(hass: HomeAssistant) -> None:
    """Unregister Lucia services."""
    hass.services.async_remove(DOMAIN, "send_message")


async def async_setup_websocket_api(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """Set up the WebSocket API for real-time communication."""
    _LOGGER.debug("WebSocket API setup for entry %s", entry.entry_id)


async def async_reload_entry(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """Reload config entry."""
    await async_unload_entry(hass, entry)
    await async_setup_entry(hass, entry)
