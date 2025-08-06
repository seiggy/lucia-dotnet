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

async def validate_input(hass: HomeAssistant, data: dict[str, Any]) -> None:
    """Validate the user input and connection to the A2A service.
    data has the keys from STEP_USER_DATA_SCHEMA with the values provided by the user.
    """
    
    client = A2ACardResolver(
        httpx_client = get_async_client(hass),
        base_url = data[CONF_REPOSITORY],
        api_key = data[CONF_API_KEY],
    )
    
    agent_card: AgentCard | None = None
    
    try:
        _LOGGER.info("Resolving agent card from %s", data[CONF_REPOSITORY])
        await hass.async_add_executor_job(
            client.get_agent_card()
        )
    except Exception as e:
        _LOGGER.error(
            f'Failed to resolve agent card: {e}. Please check your repository and API key.'
            exc_info=True
        )
        raise ValueError("Invalid repository or API key") from e

