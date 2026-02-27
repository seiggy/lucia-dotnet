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
        vol.Optional(CONF_API_KEY, default=""): str,
    }
)

async def validate_input(hass: HomeAssistant, data: dict[str, Any]) -> dict[str, Any]:
    """Validate the connection by fetching the agent catalog from /agents.

    Uses httpx directly (no a2a-sdk) to avoid blocking and async issues.
    """
    import httpx

    repository = data[CONF_REPOSITORY].rstrip("/")
    catalog_url = f"{repository}/agents"
    headers = {}
    if data.get(CONF_API_KEY):
        headers["X-Api-Key"] = data[CONF_API_KEY]

    async with httpx.AsyncClient(
        headers=headers,
        verify=False,
        timeout=30.0,
    ) as client:
        try:
            response = await client.get(catalog_url)
        except httpx.ConnectError as err:
            _LOGGER.error("Cannot connect to %s: %s", catalog_url, err)
            raise ValueError("Cannot connect to repository. Check URL and network.") from err
        except Exception as err:
            _LOGGER.error("Failed to fetch catalog from %s: %s", catalog_url, err)
            raise ValueError("Invalid repository or API key") from err

        if response.status_code == 401:
            raise ValueError("Authentication failed (401). Check your API key.")

        response.raise_for_status()

        raw = response.json()
        agents = raw if isinstance(raw, list) else raw.get("agents") or raw.get("catalog") or raw.get("value")
        if not isinstance(agents, list) or len(agents) == 0:
            raise ValueError(
                "No agents in catalog. Ensure Lucia has finished starting and has agents registered."
            )

        first = agents[0]
        return {
            "title": first.get("name", "Lucia Agent"),
            "agent_id": first.get("id", "lucia"),
        }

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
        return LuciaOptionsFlow(config_entry)


class LuciaOptionsFlow(OptionsFlow):
    """Handle options for Lucia integration."""

    def __init__(self, config_entry: ConfigEntry) -> None:
        """Initialize options flow."""
        super().__init__()
        self.config_entry = config_entry

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
