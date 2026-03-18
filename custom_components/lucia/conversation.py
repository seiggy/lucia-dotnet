"""Conversation platform for Lucia integration."""
from __future__ import annotations

import logging
import uuid
from typing import Any, Literal

from homeassistant.components import conversation
from homeassistant.components.conversation import (
    ConversationInput,
    ConversationResult,
)

# AssistantContent and ChatLog added in HA 2026.1; optional for older versions
try:
    from homeassistant.components.conversation import AssistantContent, ChatLog
    _HAS_CHAT_LOG_API = True
except ImportError:
    try:
        from homeassistant.components.conversation.chat_log import (
            AssistantContent,
            ChatLog,
        )
        _HAS_CHAT_LOG_API = True
    except ImportError:
        AssistantContent = None  # type: ignore[misc, assignment]
        ChatLog = object  # type: ignore[misc, assignment] - placeholder when unavailable
        _HAS_CHAT_LOG_API = False
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.helpers import area_registry, device_registry, intent
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from .const import (
    CONF_PROMPT_OVERRIDE,
    DOMAIN,
)
from .conversation_tracker import ConversationTracker
from .fast_conversation import send_conversation

_LOGGER = logging.getLogger(__name__)


class _NoOpChatLog:
    """No-op chat log for HA versions that use async_process without ChatLog."""

    def async_add_assistant_content_without_tools(self, *args: Any, **kwargs: Any) -> None:
        """No-op: older HA does not provide chat log in async_process."""


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
        self._tracker = ConversationTracker(ttl_seconds=300.0)
        self._attr_device_info = {
            "identifiers": {(DOMAIN, entry.entry_id)},
            "name": "Lucia Home Agent",
            "manufacturer": "Lucia",
            "model": "Conversation Agent",
            "sw_version": "1.2.0",
        }

    @property
    def supported_languages(self) -> list[str] | Literal["*"]:
        """Return supported languages."""
        return "*"

    async def async_process(
        self, user_input: ConversationInput
    ) -> ConversationResult:
        """Process a sentence. Required by HA versions where this is the abstract method."""
        try:
            from homeassistant.helpers.chat_session import async_get_chat_session
            from homeassistant.components.conversation.chat_log import async_get_chat_log

            with (
                async_get_chat_session(self.hass, user_input.conversation_id) as session,
                async_get_chat_log(self.hass, session, user_input) as chat_log,
            ):
                return await self._async_handle_message(user_input, chat_log)
        except (ImportError, AttributeError):
            return await self._async_handle_message(user_input, _NoOpChatLog())

    async def _async_handle_message(
        self,
        user_input: ConversationInput,
        chat_log: ChatLog,
    ) -> ConversationResult:
        """Handle an incoming conversation message with chat log support."""
        client_data = self.hass.data[DOMAIN].get(self.entry.entry_id)

        if not client_data:
            _LOGGER.error("No client data found for conversation processing")
            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech(
                "I'm sorry, but I'm not properly configured. Please check the integration settings."
            )
            return ConversationResult(
                response=intent_response,
                conversation_id=None,
                continue_conversation=False,
            )

        httpx_client = client_data.get("httpx_client")
        base_url = client_data.get("repository")

        if not base_url or not httpx_client:
            _LOGGER.error("Missing repository URL or HTTP client")
            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech("Agent configuration is incomplete.")
            return ConversationResult(
                response=intent_response,
                conversation_id=None,
                continue_conversation=False,
            )

        # Resolve conversation ID from tracker or create new
        tracked = None
        if user_input.conversation_id:
            tracked = self._tracker.get(user_input.conversation_id)

        conversation_id = tracked.context_id if tracked else None
        ha_conversation_id = user_input.conversation_id or ""

        # Generate a stable conversation ID for the first turn
        if not conversation_id:
            conversation_id = uuid.uuid4().hex

        # Extract device context from HA ConversationInput
        device_id = None
        device_area = None
        device_type = None
        user_id = None

        if hasattr(user_input, "device_id") and user_input.device_id:
            device_id = user_input.device_id
            dev_reg = device_registry.async_get(self.hass)
            if dev_reg:
                device = dev_reg.async_get(device_id)
                if device:
                    if device.area_id:
                        area_reg = area_registry.async_get(self.hass)
                        if area_reg:
                            area = area_reg.async_get_area(device.area_id)
                            if area:
                                device_area = area.name
                    if device.model:
                        device_type = device.model

        # HA doesn't expose user_id directly on ConversationInput in all versions
        if hasattr(user_input, "context") and user_input.context:
            user_id = getattr(user_input.context, "user_id", None)

        location = self.hass.config.location_name or "Home"

        # Get optional prompt override from options
        options = self.entry.options or {}
        prompt_override = options.get(CONF_PROMPT_OVERRIDE) or None

        try:
            _LOGGER.debug("Sending conversation request: %s", user_input.text)

            result = await send_conversation(
                client=httpx_client,
                base_url=base_url,
                text=user_input.text,
                conversation_id=conversation_id,
                device_id=device_id,
                device_area=device_area,
                device_type=device_type,
                user_id=user_id,
                location=location,
                prompt_override=prompt_override,
            )

            response_text = result.text
            continue_conversation = result.needs_input

            # Update tracker with returned conversationId for multi-turn
            returned_conv_id = result.conversation_id or conversation_id
            if returned_conv_id and ha_conversation_id:
                self._tracker.store(
                    ha_conversation_id,
                    context_id=returned_conv_id,
                )

            _LOGGER.debug(
                "Received response (type=%s): %s",
                result.response_type,
                response_text[:100],
            )

            # Add the response to the chat log for multi-turn support
            if _HAS_CHAT_LOG_API and AssistantContent:
                chat_log.async_add_assistant_content_without_tools(
                    AssistantContent(
                        agent_id=user_input.agent_id,
                        content=response_text,
                    )
                )

            # Fire event so automations can trigger re-listen
            if continue_conversation:
                self.hass.bus.async_fire(
                    "lucia_conversation_input_required",
                    {
                        "conversation_id": ha_conversation_id,
                        "agent_id": getattr(user_input, "agent_id", None),
                        "response_preview": (
                            (response_text[:200] + "\u2026")
                            if len(response_text) > 200
                            else response_text
                        ),
                    },
                )

            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech(response_text)

            return ConversationResult(
                response=intent_response,
                conversation_id=ha_conversation_id,
                continue_conversation=continue_conversation,
            )

        except Exception as err:
            _LOGGER.error("Error processing conversation: %s", err, exc_info=True)

            error_text = f"I encountered an error while processing your request: {err}"
            if _HAS_CHAT_LOG_API and AssistantContent:
                chat_log.async_add_assistant_content_without_tools(
                    AssistantContent(
                        agent_id=user_input.agent_id,
                        content=error_text,
                    )
                )

            intent_response = intent.IntentResponse(language=user_input.language)
            intent_response.async_set_speech(error_text)

            return ConversationResult(
                response=intent_response,
                conversation_id=None,
                continue_conversation=False,
            )

    async def async_added_to_hass(self) -> None:
        """When entity is added to Home Assistant."""
        await super().async_added_to_hass()

        conversation.async_set_agent(
            self.hass,
            self.entry,
            self,
        )

        _LOGGER.info("Lucia conversation agent registered")
