"""Config flow for Lucia integration."""
from __future__ import annotations

import logging
from typing import Any

import voluptuous as vol

from homeassistant.config_entries import (
    ConfigEntry,
    ConfigFlow,
    ConfigFlowResult,
    OptionsFlow,
)
from homeassistant.const import CONF_API_KEY
from homeassistant.core import HomeAssistant
from homeassistant.helpers.httpx_client import get_async_client
from homeassistant.helpers.selector import (
    NumberSelector,
    NumberSelectorConfig,
    SelectSelector,
    SelectSelectorConfig,
    SelectSelectorMode,
    TemplateSelector,
)

from .const import (
    CONF_AGENT_NAME,
    CONF_MAX_TOKENS,
    CONF_PROMPT,
    CONF_REPOSITORY,
    DOMAIN,
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
    # Import here to avoid circular imports
    import httpx
    from a2a.client import A2ACardResolver

    # Create a dedicated HTTP client for the A2A repository with API key header
    headers = {}
    if data.get(CONF_API_KEY):
        headers["X-Api-Key"] = data[CONF_API_KEY]

    httpx_client = httpx.AsyncClient(headers=headers)

    try:
        # Create resolver with base URL
        client = A2ACardResolver(
            httpx_client=httpx_client,
            base_url=data[CONF_REPOSITORY],
        )

        _LOGGER.info("Resolving agent card from %s", data[CONF_REPOSITORY])
        agent_card = await hass.async_add_executor_job(client.get_agent_card)

        if not agent_card:
            raise ValueError("Failed to retrieve agent card")

        # Return info to store in config entry
        return {
            "title": agent_card.name if hasattr(agent_card, "name") else "Lucia Agent",
            "agent_id": agent_card.id if hasattr(agent_card, "id") else "lucia",
        }
    except Exception as err:
        _LOGGER.error(
            "Failed to resolve agent card: %s. Please check your repository and API key.",
            err,
            exc_info=True,
        )
        raise ValueError("Invalid repository or API key") from err
    finally:
        # Clean up the HTTP client
        await httpx_client.aclose()

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

                # Set unique ID based on repository URL to prevent duplicates
                await self.async_set_unique_id(user_input[CONF_REPOSITORY])
                self._abort_if_unique_id_configured()

                return self.async_create_entry(
                    title=info["title"],
                    data={
                        CONF_REPOSITORY: user_input[CONF_REPOSITORY],
                        CONF_API_KEY: user_input[CONF_API_KEY],
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
        return LuciaOptionsFlow()


class LuciaOptionsFlow(OptionsFlow):
    """Handle options for Lucia integration."""

    def __init__(self) -> None:
        """Initialize options flow."""
        # Configuration entry is provided by OptionsFlow base class at runtime.
        super().__init__()

    async def async_step_init(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Manage the options."""
        if user_input is not None:
            return self.async_create_entry(title="", data=user_input)

        # Get the agent catalog from hass.data
        entry_data = self.hass.data.get(DOMAIN, {}).get(self.config_entry.entry_id, {})
        catalog = entry_data.get("catalog", [])

        # Build agent selection options
        agent_options = []
        for agent in catalog:
            agent_name = agent.get("name", "unknown")
            agent_description = agent.get("description", "")
            # Create display label with name and description
            label = f"{agent_name}"
            if agent_description:
                label = f"{agent_name} - {agent_description}"
            agent_options.append({
                "value": agent_name,
                "label": label
            })

        # If no agents found in catalog, show error
        if not agent_options:
            _LOGGER.warning("No agents available in catalog for options flow")
            agent_options = [{"value": "none", "label": "No agents available"}]

        options = self.config_entry.options or {}

        # Get current agent name, default to first agent in catalog
        current_agent = options.get(CONF_AGENT_NAME)
        if not current_agent and catalog:
            current_agent = catalog[0].get("name", "")

        return self.async_show_form(
            step_id="init",
            data_schema=vol.Schema({
                vol.Optional(
                    CONF_AGENT_NAME,
                    default=current_agent,
                ): SelectSelector(
                    SelectSelectorConfig(
                        options=agent_options,
                        mode=SelectSelectorMode.DROPDOWN,
                    )
                ),
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
