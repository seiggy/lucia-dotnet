"""The Lucia Home Agent integration."""
from __future__ import annotations

import logging
from typing import Any

import httpx
from a2a.client import A2ACardResolver, A2AClient
from a2a.types import AgentCard

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ConfigEntryNotReady
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers.typing import ConfigType

from .const import (
    CONF_API_KEY,
    CONF_REPOSITORY,
    DOMAIN,
    NAME,
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

    # Get configuration from the config entry
    repository = entry.data.get(CONF_REPOSITORY)
    api_key = entry.data.get(CONF_API_KEY)

    if not repository or not api_key:
        _LOGGER.error("Missing required configuration: repository or API key")
        return False

    # Initialize the A2A client
    try:
        # Create HTTP client with X-Api-Key authentication header
        headers = {"X-Api-Key": api_key}
        httpx_client = httpx.AsyncClient(headers=headers)

        # Create the A2A card resolver
        resolver = A2ACardResolver(
            httpx_client=httpx_client,
            base_url=repository,
        )

        # Get the agent card to verify connection
        agent_card = await hass.async_add_executor_job(resolver.get_agent_card)

        if not agent_card:
            _LOGGER.error("Failed to retrieve agent card from %s", repository)
            await httpx_client.aclose()
            raise ConfigEntryNotReady(f"Could not connect to Lucia agent at {repository}")

        # Create the A2A client with the same httpx client
        client = A2AClient(
            httpx_client=httpx_client,
            base_url=repository,
        )

        # Store the client, httpx_client, agent card, and resolver in hass.data
        hass.data[DOMAIN][entry.entry_id] = {
            "client": client,
            "agent_card": agent_card,
            "resolver": resolver,
            "httpx_client": httpx_client,  # Store for cleanup
        }

        _LOGGER.info(
            "Successfully connected to Lucia agent: %s (version: %s)",
            agent_card.name,
            agent_card.version if hasattr(agent_card, 'version') else "unknown"
        )

    except Exception as err:
        _LOGGER.error("Failed to set up Lucia integration: %s", err)
        raise ConfigEntryNotReady(f"Error connecting to Lucia agent: {err}") from err

    # Set up platforms
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)

    # Register services
    await async_register_services(hass, entry)

    # Set up WebSocket API if needed
    await async_setup_websocket_api(hass, entry)

    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Unload a config entry."""
    # Unload platforms
    unload_ok = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)

    if unload_ok:
        # Clean up the httpx client
        entry_data = hass.data[DOMAIN].get(entry.entry_id)
        if entry_data and "httpx_client" in entry_data:
            await entry_data["httpx_client"].aclose()

        # Clean up stored data
        hass.data[DOMAIN].pop(entry.entry_id, None)

        # Unregister services if this was the last entry
        if not hass.data[DOMAIN]:
            await async_unregister_services(hass)

    return unload_ok


async def async_register_services(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """Register Lucia services."""

    async def handle_send_message(call):
        """Handle the send_message service."""
        message = call.data.get("message")
        agent_id = call.data.get("agent_id")

        client_data = hass.data[DOMAIN].get(entry.entry_id)
        if not client_data:
            _LOGGER.error("No client available for service call")
            return

        client = client_data["client"]

        try:
            response = await hass.async_add_executor_job(
                client.send_message,
                agent_id,
                message,
            )
            _LOGGER.debug("Message sent successfully: %s", response)
            return response
        except Exception as err:
            _LOGGER.error("Failed to send message: %s", err)
            raise

    # Register service only if not already registered
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
    # This would set up WebSocket handlers for real-time communication
    # with the Lucia agent system. Implementation depends on specific requirements.
    _LOGGER.debug("WebSocket API setup for entry %s", entry.entry_id)
    # TODO: Implement WebSocket handlers for real-time agent communication


async def async_reload_entry(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """Reload config entry."""
    await async_unload_entry(hass, entry)
    await async_setup_entry(hass, entry)
