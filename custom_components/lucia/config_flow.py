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
        vol.Required(CONF_API_KEY): str,
    }
)

async def validate_input(hass: HomeAssistant, data: dict[str, Any]) -> dict[str, Any]:
    """Validate the user input and connection to the Lucia agent service.

    Data has the keys from STEP_USER_DATA_SCHEMA with values provided by the user.
    """
    import httpx

    # Create a dedicated HTTP client with API key header and redirect support
    headers = {}
    if data.get(CONF_API_KEY):
        headers["X-Api-Key"] = data[CONF_API_KEY]

    httpx_client = httpx.AsyncClient(
        headers=headers, verify=False, timeout=30.0, follow_redirects=True
    )

    try:
        # Fetch the agent catalog to validate connectivity and discover agents
        base_url = data[CONF_REPOSITORY].rstrip("/")
        # Strip trailing /agents if user pasted the full catalog URL
        if base_url.endswith("/agents"):
            base_url = base_url[: -len("/agents")]
        catalog_url = f"{base_url}/agents"

        _LOGGER.info("Fetching agent catalog from %s", catalog_url)
        response = await httpx_client.get(catalog_url)
        response.raise_for_status()

        agents = response.json()

        if not isinstance(agents, list):
            raise ValueError("Invalid agent catalog response")

        if not agents:
            _LOGGER.warning("Agent catalog is empty — agents may still be starting")

        # Use the first agent's name as the title, or fallback
        title = "Lucia Agent"
        agent_id = "lucia"
        if agents:
            first = agents[0]
            title = first.get("name", title)
            agent_id = first.get("name", agent_id)

        return {
            "title": title,
            "agent_id": agent_id,
        }
    except httpx.HTTPStatusError as err:
        _LOGGER.error(
            "Failed to fetch agent catalog (HTTP %s): %s",
            err.response.status_code,
            err,
        )
        raise ValueError("Invalid repository or API key") from err
    except Exception as err:
        _LOGGER.error(
            "Failed to connect to Lucia: %s. Please check your repository URL and API key.",
            err,
            exc_info=True,
        )
        raise ValueError("Invalid repository or API key") from err
    finally:
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
