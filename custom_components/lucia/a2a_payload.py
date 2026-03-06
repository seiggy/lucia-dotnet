"""Utilities for building A2A conversation payloads."""
from __future__ import annotations

from typing import Any


def build_outbound_user_text(
    *,
    user_text: str,
    system_prompt: str,
    is_new_conversation: bool,
) -> str:
    """Build outbound text for A2A message payloads.

    New conversations include the rendered Home Assistant context prompt.
    Follow-up turns send only the user's latest utterance because conversation
    continuity should come from contextId/session history.
    """
    if is_new_conversation:
        return f"{system_prompt}\n\nUser: {user_text}"

    return user_text


def build_a2a_user_message(
    *,
    user_text: str,
    system_prompt: str,
    context_id: str,
    is_new_conversation: bool,
) -> dict[str, Any]:
    """Create the JSON-RPC message object for message/send."""
    message_text = build_outbound_user_text(
        user_text=user_text,
        system_prompt=system_prompt,
        is_new_conversation=is_new_conversation,
    )

    return {
        "kind": "message",
        "role": "user",
        "parts": [
            {
                "kind": "text",
                "text": message_text,
                "metadata": None,
            }
        ],
        "messageId": None,
        "contextId": context_id,
        # Task rehydration is currently context/session-driven in this flow.
        "taskId": None,
        "metadata": None,
        "referenceTaskIds": [],
        "extensions": [],
    }
