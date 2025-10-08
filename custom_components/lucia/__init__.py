"""The Lucia Home Agent integration."""
from __future__ import annotations

import json
import logging
from typing import Any

import httpx

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import ConfigEntryNotReady
from homeassistant.helpers import config_validation as cv
from homeassistant.helpers.typing import ConfigType

from .const import (
    CONF_AGENT_NAME,
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

    if not repository:
        _LOGGER.error("Missing required configuration: repository URL")
        return False

    # Initialize the HTTP client
    try:
        # Create HTTP client with optional X-Api-Key authentication header
        headers = {}
        if api_key:
            headers["X-Api-Key"] = api_key

        httpx_client = httpx.AsyncClient(
            headers=headers,
            verify=False,  # For development with self-signed certs
            timeout=30.0
        )

        # Fetch the agent catalog
        _LOGGER.info("Fetching agent catalog from %s", repository)
        catalog_url = f"{repository}/agents"

        try:
            catalog_response = await httpx_client.get(catalog_url)
            catalog_response.raise_for_status()

            agents = catalog_response.json()

            if not agents or not isinstance(agents, list):
                _LOGGER.error("Invalid agent catalog response from %s", catalog_url)
                await httpx_client.aclose()
                raise ConfigEntryNotReady(f"Invalid agent catalog from {repository}")

            _LOGGER.info("Discovered %d agent(s) from catalog", len(agents))

            # Check if user has selected a specific agent in options
            options = entry.options or {}
            selected_agent_name = options.get(CONF_AGENT_NAME)
            
            # Find the selected agent, or use first agent as default
            agent_card = None
            if selected_agent_name:
                # Find agent by name
                for agent in agents:
                    if agent.get("name") == selected_agent_name:
                        agent_card = agent
                        _LOGGER.info("Using user-selected agent: %s", selected_agent_name)
                        break
                
                if not agent_card:
                    _LOGGER.warning(
                        "Selected agent '%s' not found in catalog, using first agent",
                        selected_agent_name
                    )
            
            # Fall back to first agent if no selection or not found
            if not agent_card:
                if not agents:
                    _LOGGER.error("No agents found in catalog")
                    await httpx_client.aclose()
                    raise ConfigEntryNotReady(f"No agents available at {repository}")
                agent_card = agents[0]
                _LOGGER.info("Using default agent (first in catalog)")

            agent_name = agent_card.get("name", "unknown")
            agent_version = agent_card.get("version", "unknown")
            agent_relative_url = agent_card.get("url", "")

            # Convert relative URL to absolute
            if agent_relative_url.startswith("/"):
                agent_url = f"{repository}{agent_relative_url}"
            else:
                agent_url = agent_relative_url

            _LOGGER.info(
                "Using agent: %s (version: %s) at %s",
                agent_name,
                agent_version,
                agent_url
            )

        except httpx.HTTPStatusError as err:
            _LOGGER.error("Failed to fetch agent catalog (HTTP %s): %s", err.response.status_code, err)
            await httpx_client.aclose()
            raise ConfigEntryNotReady(f"Could not connect to agent repository at {repository}") from err
        except Exception as err:
            _LOGGER.error("Failed to fetch agent catalog: %s", err)
            await httpx_client.aclose()
            raise ConfigEntryNotReady(f"Error fetching agent catalog: {err}") from err

        # Store the httpx_client, agent card, agent URL, and catalog in hass.data
        hass.data[DOMAIN][entry.entry_id] = {
            "httpx_client": httpx_client,
            "agent_card": agent_card,
            "agent_url": agent_url,
            "catalog": agents,  # Store full catalog for future agent selection
            "repository": repository,
        }

    except Exception as err:
        _LOGGER.error("Failed to set up Lucia integration: %s", err)
        raise ConfigEntryNotReady(f"Error connecting to Lucia agent: {err}") from err

    # Set up platforms
    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)

    # Register services
    await async_register_services(hass, entry)

    # Set up WebSocket API if needed
    await async_setup_websocket_api(hass, entry)
    
    # Register options update listener to reload when agent selection changes
    entry.async_on_unload(entry.add_update_listener(async_reload_entry))

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
