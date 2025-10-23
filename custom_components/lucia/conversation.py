"""Conversation platform for Lucia integration."""
from __future__ import annotations

import json
import logging
import uuid
from datetime import datetime
from typing import Any, Literal

from homeassistant.components import conversation
from homeassistant.components.conversation import ConversationResult
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.exceptions import HomeAssistantError, TemplateError
from homeassistant.helpers import area_registry, device_registry, entity_registry, intent, template
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.util import ulid, dt as dt_util

from .const import (
    CONF_MAX_TOKENS,
    CONF_PROMPT,
    CONF_TEMPERATURE,
    CONF_TOP_P,
    DEFAULT_MAX_TOKENS,
    DEFAULT_PROMPT,
    DEFAULT_TEMPERATURE,
    DEFAULT_TOP_P,
    DOMAIN,
)

_LOGGER = logging.getLogger(__name__)


async def async_setup_entry(
    hass: HomeAssistant,
    config_entry: ConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up conversation platform from a config entry."""
    agent = LuciaConversationEntity(config_entry)
    async_add_entities([agent])


class LuciaConversationEntity(conversation.ConversationEntity):
    """Lucia conversation agent entity."""

    _attr_has_entity_name = True
    _attr_name = None

    def __init__(self, entry: ConfigEntry) -> None:
        """Initialize the conversation entity."""
        self.entry = entry
        self._attr_unique_id = f"{entry.entry_id}"
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "Lucia Home Agent",
            "manufacturer": "Lucia",
            "model": "A2A Agent",
            "sw_version": "1.0.0",
        }

    @property
    def supported_languages(self) -> list[str] | Literal["*"]:
        """Return supported languages."""
        return "*"  # Support all languages

    async def async_process(
        self, user_input: conversation.ConversationInput
    ) -> conversation.ConversationResult:
        """Process a conversation turn."""
        client_data = self.hass.data[DOMAIN].get(self.entry.entry_id)

        if not client_data:
            _LOGGER.error("No client data found for conversation processing")
            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech("I'm sorry, but I'm not properly configured. Please check the integration settings.")
            return conversation.ConversationResult(
                response=intent_response,
                conversation_id=None,
            )

        agent_card = client_data.get("agent_card")
        httpx_client = client_data.get("httpx_client")
        agent_url = client_data.get("agent_url")

        if not agent_url or not httpx_client:
            _LOGGER.error("Missing agent URL or HTTP client")
            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech("Agent configuration is incomplete.")
            return conversation.ConversationResult(
                response=intent_response,
                conversation_id=None,
            )

        # Get configuration options
        options = self.entry.options or self.entry.data
        prompt_template = options.get(CONF_PROMPT, DEFAULT_PROMPT)

        # Process the prompt template if it exists
        try:
            if prompt_template:
                system_prompt = self._render_template(prompt_template, user_input)
            else:
                system_prompt = DEFAULT_PROMPT
        except TemplateError as err:
            _LOGGER.error("Error rendering prompt template: %s", err)
            system_prompt = DEFAULT_PROMPT

        # Generate IDs for message tracking
        message_id = str(uuid.uuid4())

        # Use conversation_id as context_id for threading
        if user_input.conversation_id:
            context_id = user_input.conversation_id
        else:
            context_id = str(uuid.uuid4())

        # Build the message content (combining system prompt + user input)
        message_text = f"{system_prompt}\n\nUser: {user_input.text}"

        # Create the message structure (without taskId - not supported)
        message = {
            "kind": "message",
            "role": "user",
            "parts": [
                {
                    "kind": "text",
                    "text": message_text,
                    "metadata": None
                }
            ],
            "messageId": None,
            "contextId": context_id,
            "taskId": None,  # Task management not supported by Agent Framework
            "metadata": None,
            "referenceTaskIds": [],
            "extensions": []
        }

        # Create JSON-RPC 2.0 request
        jsonrpc_request = {
            "jsonrpc": "2.0",
            "method": "message/send",
            "params": {
                "message": message
            },
            "id": 1
        }

        try:
            _LOGGER.debug("Sending message to agent at %s: %s", agent_url, user_input.text)

            # Send JSON-RPC request to agent
            response = await httpx_client.post(
                agent_url,
                json=jsonrpc_request,
                timeout=30.0
            )

            if response.status_code != 200:
                _LOGGER.error("Agent returned status %s: %s", response.status_code, response.text[:200])
                return conversation.ConversationResult(
                    response=intent.IntentResponse(language=user_input.language),
                    conversation_id=context_id,
                )
                # Note: IntentResponse will be converted to error speech by Home Assistant

            # Parse JSON-RPC response
            result = response.json()

            # Check for JSON-RPC error
            if "error" in result:
                error_msg = result["error"].get("message", "Unknown error")
                _LOGGER.error("Agent returned error: %s", error_msg)
                # Create an intent response with error speech
                intent_response = intent.IntentResponse(language=user_input.language)
                intent_response.async_set_speech(f"Agent error: {error_msg}")
                return conversation.ConversationResult(
                    response=intent_response,
                    conversation_id=context_id,
                )

            # Extract response text from result
            response_text = ""
            if "result" in result and isinstance(result["result"], dict):
                agent_message = result["result"]
                if "parts" in agent_message:
                    for part in agent_message["parts"]:
                        if part.get("kind") == "text":
                            response_text += part.get("text", "")

            if not response_text:
                response_text = "I received your message but didn't generate a response."

            _LOGGER.debug("Received response from agent: %s", response_text[:100])

            # Create the conversation result with intent response
            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech(response_text)

            return conversation.ConversationResult(
                response=intent_response,
                conversation_id=context_id,  # Return the context_id for threading
            )

        except Exception as err:
            _LOGGER.error("Error processing conversation with agent: %s", err, exc_info=True)

            # Create error intent response
            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech(f"I encountered an error while processing your request: {str(err)}")

            return conversation.ConversationResult(
                response=intent_response,
                conversation_id=None,
            )

    def _render_template(
        self,
        prompt_template: str,
        user_input: conversation.ConversationInput,
    ) -> str:
        """Render a template with the current context."""
        raw_prompt = template.Template(prompt_template, self.hass)

        # Get exposed entities for conversation
        exposed_entities = self._get_exposed_entities(user_input)

        # Get device context from the conversation input
        device_id = None
        device_area = "unknown"
        device_type = "unknown"
        device_capabilities = []

        # Extract device information from conversation input
        if hasattr(user_input, 'device_id') and user_input.device_id:
            device_id = user_input.device_id
            # Get device registry to find device details
            dev_reg = device_registry.async_get(self.hass)
            if dev_reg:
                device = dev_reg.async_get(device_id)
                if device:
                    # Get area information
                    if device.area_id:
                        area_reg = area_registry.async_get(self.hass)
                        if area_reg:
                            area = area_reg.async_get_area(device.area_id)
                            if area:
                                device_area = area.name

                    # Determine device type and capabilities
                    if device.model:
                        device_type = device.model
                    elif device.name:
                        device_type = device.name

                    # Check for device capabilities based on integrations
                    if "esphome" in device.identifiers:
                        device_capabilities.append("microphone")
                        device_capabilities.append("speaker")
                    if "assist_satellite" in device.identifiers:
                        device_capabilities.append("voice_assistant")
                    # Add more capability detection as needed

        # Build template variables with full context including device info
        template_vars = {
            "ha_name": self.hass.config.location_name or "Home",
            "language": user_input.language,
            "exposed_entities": exposed_entities,
            "now": dt_util.now,
            "areas": self._get_areas,
            "device_id": device_id or "unknown",
            "device_area": device_area,
            "device_type": device_type,
            "device_capabilities": device_capabilities,
            "entity_area": lambda entity_id: self._get_entity_area(entity_id),
        }

        return raw_prompt.async_render(template_vars, parse_result=False)

    def _get_exposed_entities(
        self, user_input: conversation.ConversationInput
    ) -> list[dict[str, Any]]:
        """Get all entities exposed to the conversation agent."""
        entities = []

        # Get all entities that should be exposed to the assistant
        # This follows Home Assistant's conversation entity exposure logic
        ent_reg = entity_registry.async_get(self.hass)

        for state in self.hass.states.async_all():
            # Check if entity should be exposed to conversation
            if not self._should_expose_entity(state.entity_id):
                continue

            # Get entity info
            entity_info = {
                "entity_id": state.entity_id,
                "name": state.attributes.get("friendly_name", state.entity_id),
                "state": state.state,
                "attributes": state.attributes,
                "aliases": [],  # Would come from entity registry if available
            }

            # Add entity registry aliases if available
            if ent_reg:
                entry = ent_reg.async_get(state.entity_id)
                if entry and entry.aliases:
                    entity_info["aliases"] = list(entry.aliases)

            entities.append(entity_info)

        return entities

    def _should_expose_entity(self, entity_id: str) -> bool:
        """Determine if an entity should be exposed to conversation."""
        # Get the entity state
        state = self.hass.states.get(entity_id)
        if not state:
            return False

        # Check if entity is hidden
        if state.attributes.get("hidden"):
            return False

        # Check conversation exposure settings
        # By default, expose common domains
        domain = entity_id.split(".")[0]
        exposed_domains = [
            "light", "switch", "fan", "cover", "climate", "lock",
            "media_player", "scene", "script", "automation", "vacuum",
            "sensor", "binary_sensor", "device_tracker", "person",
            "weather", "alarm_control_panel", "humidifier", "input_boolean",
            "input_number", "input_select", "input_text", "timer", "counter"
        ]

        return domain in exposed_domains

    def _get_areas(self) -> list[str]:
        """Get all areas in Home Assistant."""
        area_reg = area_registry.async_get(self.hass)
        if not area_reg:
            return []

        areas = []
        for area in area_reg.async_list_areas():
            areas.append(area.name)

        return areas

    def _get_entity_area(self, entity_id: str) -> str | None:
        """Get the area of an entity."""
        ent_reg = entity_registry.async_get(self.hass)
        area_reg = area_registry.async_get(self.hass)

        if not ent_reg or not area_reg:
            return None

        entity = ent_reg.async_get(entity_id)
        if not entity:
            return None

        # Check if entity has direct area assignment
        if entity.area_id:
            area = area_reg.async_get_area(entity.area_id)
            if area:
                return area.name

        # Check if entity's device has area assignment
        if entity.device_id:
            dev_reg = device_registry.async_get(self.hass)
            if dev_reg:
                device = dev_reg.async_get(entity.device_id)
                if device and device.area_id:
                    area = area_reg.async_get_area(device.area_id)
                    if area:
                        return area.name

        return None

    async def async_added_to_hass(self) -> None:
        """When entity is added to Home Assistant."""
        await super().async_added_to_hass()

        # Register the conversation agent
        conversation.async_set_agent(
            self.hass,
            self.entry,
            self,
        )

        _LOGGER.info("Lucia conversation agent registered")

    async def async_will_remove_from_hass(self) -> None:
        """When entity will be removed from Home Assistant."""
        conversation.async_unset_agent(self.hass, self.entry)
        await super().async_will_remove_from_hass()
