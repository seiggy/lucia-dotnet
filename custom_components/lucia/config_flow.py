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
    TemplateSelector,
)

from .const import (
    CONF_PROMPT_OVERRIDE,
    CONF_REPOSITORY,
    CONF_VERIFY_SSL,
    DOMAIN,
)

_LOGGER = logging.getLogger(__name__)

STEP_USER_DATA_SCHEMA = vol.Schema(
    {
        vol.Required(CONF_REPOSITORY): str,
        vol.Optional(CONF_API_KEY, default=""): str,
        vol.Optional(CONF_VERIFY_SSL, default=False): bool,
    }
)


async def validate_input(hass: HomeAssistant, data: dict[str, Any]) -> dict[str, Any]:
    """Validate the connection by probing the conversation endpoint.

    Uses httpx directly to avoid blocking and async issues.
    """
    import httpx

    repository = data[CONF_REPOSITORY].rstrip("/")
    headers = {}
    if data.get(CONF_API_KEY):
        headers["X-Api-Key"] = data[CONF_API_KEY]

    verify_ssl = data.get(CONF_VERIFY_SSL, False)

    async with httpx.AsyncClient(
        headers=headers,
        verify=verify_ssl,
        follow_redirects=True,
        timeout=15.0,
    ) as client:
        # Probe the conversation endpoint with OPTIONS or a lightweight GET
        probe_url = f"{repository}/api/conversation"
        try:
            response = await client.options(probe_url)
        except httpx.ConnectError as err:
            _LOGGER.error("Cannot connect to %s: %s", probe_url, err)
            raise ValueError("Cannot connect to Lucia. Check URL and network.") from err
        except Exception as err:
            _LOGGER.error("Failed to reach %s: %s", probe_url, err)
            raise ValueError("Cannot reach Lucia server.") from err

        if response.status_code == 401:
            raise ValueError("Authentication failed (401). Check your API key.")

        # Any 2xx/4xx (except 401) means the server is reachable — good enough
        _LOGGER.info(
            "Lucia probe at %s returned HTTP %s — server reachable",
            probe_url,
            response.status_code,
        )

    return {"title": "Lucia Home Agent"}


class LuciaConfigFlow(ConfigFlow, domain=DOMAIN):
    """Handle a config flow for Lucia."""

    VERSION = 2
    MINOR_VERSION = 1

    async def async_step_user(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Handle the initial step."""
        errors: dict[str, str] = {}

        if user_input is not None:
            try:
                info = await validate_input(self.hass, user_input)

                await self.async_set_unique_id(user_input[CONF_REPOSITORY])
                self._abort_if_unique_id_configured()

                return self.async_create_entry(
                    title=info["title"],
                    data={
                        CONF_REPOSITORY: user_input[CONF_REPOSITORY],
                        CONF_API_KEY: user_input[CONF_API_KEY],
                        CONF_VERIFY_SSL: user_input.get(CONF_VERIFY_SSL, False),
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

    async def async_step_init(
        self, user_input: dict[str, Any] | None = None
    ) -> ConfigFlowResult:
        """Manage the options."""
        if user_input is not None:
            return self.async_create_entry(title="", data=user_input)

        options = self.config_entry.options or {}

        return self.async_show_form(
            step_id="init",
            data_schema=vol.Schema(
                {
                    vol.Optional(
                        CONF_PROMPT_OVERRIDE,
                        default=options.get(CONF_PROMPT_OVERRIDE, ""),
                    ): TemplateSelector(),
                }
            ),
        )
