"""Config flow for Lucia integration."""
from __future__ import annotations
from collections.abc import Mapping
import json
import logging
from types import MappingProxyType
from typing import Any

import voluptuous as vol

from a2a.client import A2ACardResolver, A2AClient
from a2a.types import (
    AgentCard,
    MessageSendParams,
    SendMessageRequest,
    SendStreamingMessageRequest,
)
from a2a.utils.constants import (
    AGENT_CARD_WELL_KNOWN_PATH,
    EXTENDED_AGENT_CARD_PATH
)

from homeassistant.components.zone import ENTITY_ID_HOME
from homeassistant.config_entries import (
    ConfigEntry,
    ConfigFlow,
    ConfigFlowResult,
    OptionsFlow,
)
from homeassistant.const import (
    ATTR_LATITUDE,
    ATTR_LONGITUDE,
    CONF_REPOSITORY,
    CONF_API_KEY,
)

from homeassistant.core import HomeAssistant
from homeassistant.helpers.httpx_client import get_async_client
from homeassistant.helpers.selector import (
    NumberSelector,
    NumberSelectorConfig,
    SelectOptionDict,
    SelectSelector,
    SelectSelectorConfig,
    SelectSelectorMode,
    TemplateSelector,
)
from homeassistant.helpers.typing import VolDictType

from .const import (
    CONF_REPOSITORY,
    CONF_API_KEY,
    CONF_MAX_TOKENS,
    CONF_PROMPT,
)

_LOGGER = logging.getLogger(__name__)

STEP_USER_DATA_SCHEMA = vol.Schema(
    {
        vol.Required(CONF_REPOSITORY): str,
        vol.Required(CONF_API_KEY): str,
    }
)

async def validate_input(hass: HomeAssistant, data: dict[str, Any]) -> dict[str, Any]:
    """Validate the user input and connection to the A2A service.
    
    Data has the keys from STEP_USER_DATA_SCHEMA with values provided by the user.
    """
    client = A2ACardResolver(
        httpx_client=get_async_client(hass),
        base_url=data[CONF_REPOSITORY],
        api_key=data[CONF_API_KEY],
    )
    
    agent_card: AgentCard | None = None
    
    try:
        _LOGGER.info("Resolving agent card from %s", data[CONF_REPOSITORY])
        agent_card = await hass.async_add_executor_job(
            client.get_agent_card
        )
        
        if not agent_card:
            raise ValueError("Failed to retrieve agent card")
            
        # Return info to store in config entry
        return {
            "title": agent_card.name if hasattr(agent_card, 'name') else "Lucia Agent",
            "agent_id": agent_card.id if hasattr(agent_card, 'id') else "lucia",
        }
    except Exception as err:
        _LOGGER.error(
            "Failed to resolve agent card: %s. Please check your repository and API key.",
            err,
            exc_info=True,
        )
        raise ValueError("Invalid repository or API key") from err


class LuciaConfigFlow(ConfigFlow, domain=DOMAIN):
    """Handle a config flow for Lucia."""

    VERSION = 1

    async def async_step_user(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Handle the initial step."""
        errors: dict[str, str] = {}
        
        if user_input is not None:
            try:
                info = await validate_input(self.hass, user_input)
                
                # Create unique ID based on repository URL
                await self.async_set_unique_id(user_input[CONF_REPOSITORY])
                self._abort_if_unique_id_configured()
                
                return self.async_create_entry(
                    title=info["title"],
                    data={
                        **user_input,
                        "agent_id": info["agent_id"],
                    },
                )
            except ValueError:
                errors["base"] = "cannot_connect"
            except Exception:
                _LOGGER.exception("Unexpected exception")
                errors["base"] = "unknown"
        
        return self.async_show_form(
            step_id="user",
            data_schema=STEP_USER_DATA_SCHEMA,
            errors=errors,
        )
    
    async def async_step_import(self, user_input: dict[str, Any]) -> ConfigFlowResult:
        """Handle import from configuration.yaml."""
        return await self.async_step_user(user_input)
    
    @staticmethod
    def async_get_options_flow(
        config_entry: ConfigEntry,
    ) -> OptionsFlow:
        """Get the options flow for this handler."""
        return LuciaOptionsFlow(config_entry)


class LuciaOptionsFlow(OptionsFlow):
    """Handle options for Lucia integration."""
    
    def __init__(self, config_entry: ConfigEntry) -> None:
        """Initialize options flow."""
        self.config_entry = config_entry
    
    async def async_step_init(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Manage the options."""
        if user_input is not None:
            return self.async_create_entry(title="", data=user_input)
        
        options = self.config_entry.options or {}
        
        return self.async_show_form(
            step_id="init",
            data_schema=vol.Schema({
                vol.Optional(
                    CONF_PROMPT,
                    default=options.get(CONF_PROMPT, ""),
                ): TemplateSelector(),
                vol.Optional(
                    CONF_MAX_TOKENS,
                    default=options.get(CONF_MAX_TOKENS, 150),
                ): NumberSelector(
                    NumberSelectorConfig(
                        min=10,
                        max=4000,
                        step=10,
                        mode="box",
                    )
                ),
            }),
        )
