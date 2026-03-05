"""Tests for A2A payload helpers."""

from custom_components.lucia.a2a_payload import (
    build_a2a_user_message,
    build_outbound_user_text,
)


def test_build_outbound_user_text_new_conversation_includes_prompt():
    """New conversation payload should include rendered HA context."""
    result = build_outbound_user_text(
        user_text="turn on the light",
        system_prompt="HOME ASSISTANT CONTEXT",
        is_new_conversation=True,
    )

    assert result.startswith("HOME ASSISTANT CONTEXT")
    assert result.endswith("User: turn on the light")


def test_build_outbound_user_text_followup_uses_user_text_only():
    """Follow-up payload should not prepend full prompt context."""
    result = build_outbound_user_text(
        user_text="turn it on",
        system_prompt="HOME ASSISTANT CONTEXT",
        is_new_conversation=False,
    )

    assert result == "turn it on"


def test_build_a2a_user_message_uses_contextid_and_no_taskid():
    """Task ID should be omitted and contextId preserved for continuity."""
    message = build_a2a_user_message(
        user_text="turn it on",
        system_prompt="HOME ASSISTANT CONTEXT",
        context_id="ctx-123",
        is_new_conversation=False,
    )

    assert message["contextId"] == "ctx-123"
    assert message["taskId"] is None
    assert message["parts"][0]["text"] == "turn it on"
