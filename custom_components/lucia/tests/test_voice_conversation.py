"""Exercise satellite turn continuity without a Home Assistant installation."""

import importlib.util
import sys
import unittest
from pathlib import Path
from types import ModuleType, SimpleNamespace
from unittest.mock import AsyncMock, Mock, patch


class VoiceConversationTests(unittest.IsolatedAsyncioTestCase):
    async def test_first_turn_keeps_ha_and_backend_ids_for_followup(self):
        """Forward Wyoming metadata unchanged and keep the satellite listening."""
        base = Path(__file__).resolve().parent.parent
        modules = {
            name: ModuleType(name)
            for name in (
                "homeassistant",
                "homeassistant.components",
                "homeassistant.components.conversation",
                "homeassistant.config_entries",
                "homeassistant.core",
                "homeassistant.helpers",
                "homeassistant.helpers.entity_platform",
                "lucia_voice_test",
                "lucia_voice_test.const",
                "lucia_voice_test.fast_conversation",
            )
        }
        conversation = modules["homeassistant.components.conversation"]
        conversation.ConversationEntity = object
        conversation.ConversationInput = SimpleNamespace
        conversation.ConversationResult = SimpleNamespace
        conversation.AssistantContent = SimpleNamespace
        conversation.ChatLog = SimpleNamespace
        modules["homeassistant.config_entries"].ConfigEntry = SimpleNamespace
        modules["homeassistant.core"].HomeAssistant = SimpleNamespace
        modules["homeassistant.helpers.entity_platform"].AddEntitiesCallback = object
        helpers = modules["homeassistant.helpers"]
        helpers.area_registry = Mock()
        helpers.device_registry = Mock()
        helpers.device_registry.async_get.return_value = None
        helpers.intent = SimpleNamespace(IntentResponse=Mock(side_effect=lambda **kw: Mock(**kw)))
        modules["lucia_voice_test.const"].DOMAIN = "lucia"
        modules["lucia_voice_test.const"].CONF_PROMPT_OVERRIDE = "prompt_override"
        onboarding_response = SimpleNamespace(
            text="May I save your voice profile?",
            needs_input=True,
            conversation_id="voice-onboarding:backend-1",
            response_type="onboarding",
        )
        send = AsyncMock(return_value=onboarding_response)
        modules["lucia_voice_test.fast_conversation"].send_conversation = send

        with patch.dict(sys.modules, modules):
            for name in ("conversation_tracker", "conversation"):
                spec = importlib.util.spec_from_file_location(
                    f"lucia_voice_test.{name}", base / f"{name}.py"
                )
                module = importlib.util.module_from_spec(spec)
                sys.modules[spec.name] = module
                spec.loader.exec_module(module)

            clock = Mock(return_value=1000.0)
            self.enterContext(patch.object(
                sys.modules["lucia_voice_test.conversation_tracker"],
                "time",
                SimpleNamespace(monotonic=clock),
            ))
            for chat_id, pause in (
                (chat_id, pause)
                for chat_id in ("ha-session-1", None)
                for pause in (0, 360, 599)
            ):
                with self.subTest(chat_id=chat_id, pause=pause):
                    clock.return_value = 1000.0
                    send.return_value = onboarding_response
                    entity = module.LuciaConversationEntity(SimpleNamespace(entry_id="entry", options={}))
                    entity.hass = SimpleNamespace(
                        data={"lucia": {"entry": {"httpx_client": object(), "repository": "http://lucia"}}},
                        config=SimpleNamespace(location_name="Home"),
                        bus=Mock(),
                    )
                    chat_log = SimpleNamespace(
                        conversation_id=chat_id,
                        async_add_assistant_content_without_tools=Mock(),
                    )
                    user_input = SimpleNamespace(
                        conversation_id=None, device_id="satellite-1", context=None,
                        text='<lucia-voice token="0123456789abcdef0123456789abcdef" />Onboard me',
                        language="en", agent_id="lucia",
                    )
                    first = await entity._async_handle_message(user_input, chat_log)
                    self.assertTrue(first.continue_conversation)
                    self.assertTrue(first.conversation_id)
                    if chat_id:
                        self.assertEqual(chat_id, first.conversation_id)
                    self.assertEqual(user_input.text, send.call_args.kwargs["text"])
                    self.assertEqual("satellite-1", send.call_args.kwargs["device_id"])

                    user_input.conversation_id = first.conversation_id
                    user_input.text = "yes"
                    clock.return_value += pause
                    await entity._async_handle_message(user_input, chat_log)
                    self.assertEqual("voice-onboarding:backend-1", send.call_args.kwargs["conversation_id"])

                    clock.return_value += 360
                    user_input.text = "Dianna"
                    await entity._async_handle_message(user_input, chat_log)
                    self.assertEqual("voice-onboarding:backend-1", send.call_args.kwargs["conversation_id"])

                    send.return_value = SimpleNamespace(
                        text="Enrollment complete.",
                        needs_input=False,
                        conversation_id="backend-1",
                        response_type="onboarding",
                    )
                    completed = await entity._async_handle_message(user_input, chat_log)
                    self.assertFalse(completed.continue_conversation)
                    self.assertEqual("backend-1", entity._tracker.get(first.conversation_id).context_id)
                    await entity._async_handle_message(user_input, chat_log)
                    self.assertEqual("backend-1", send.call_args.kwargs["conversation_id"])
                    clock.return_value += 301
                    self.assertIsNone(entity._tracker.get(first.conversation_id))


if __name__ == "__main__":
    unittest.main()
