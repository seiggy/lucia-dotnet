"""Fast-path conversation client for the /api/conversation endpoint."""
from __future__ import annotations

import json
import logging
from dataclasses import dataclass, field
from typing import Optional

import httpx

_LOGGER = logging.getLogger(__name__)

CONVERSATION_TIMEOUT = 120.0


@dataclass
class ConversationResult:
    """Result from the conversation endpoint."""

    text: str
    response_type: str  # "command" or "llm"
    conversation_id: Optional[str] = None
    command_detail: Optional[dict] = field(default=None)
    needs_input: bool = False


async def send_conversation(
    client: httpx.AsyncClient,
    base_url: str,
    text: str,
    conversation_id: Optional[str] = None,
    device_id: Optional[str] = None,
    device_area: Optional[str] = None,
    device_type: Optional[str] = None,
    user_id: Optional[str] = None,
    location: Optional[str] = None,
    prompt_override: Optional[str] = None,
) -> ConversationResult:
    """Send a conversation request to the /api/conversation endpoint.

    Handles two response content-types:
    - application/json  → instant parsed-command result
    - text/event-stream → SSE stream for LLM fallback
    """
    from homeassistant.util import dt as dt_util

    url = f"{base_url.rstrip('/')}/api/conversation"

    body: dict = {
        "text": text,
        "context": {
            "timestamp": dt_util.utcnow().isoformat().replace("+00:00", "Z"),
            "conversationId": conversation_id,
            "deviceId": device_id,
            "deviceArea": device_area,
            "deviceType": device_type or "voice_assistant",
            "userId": user_id,
            "location": location,
        },
        "promptOverride": prompt_override,
    }

    _LOGGER.debug("POST %s with text=%r, conversationId=%s", url, text, conversation_id)

    try:
        response = await client.post(url, json=body, timeout=CONVERSATION_TIMEOUT)
        response.raise_for_status()
    except httpx.TimeoutException as err:
        _LOGGER.error("Conversation request timed out: %s", err)
        return ConversationResult(
            text="The request timed out. Please try again.",
            response_type="error",
        )
    except httpx.HTTPStatusError as err:
        _LOGGER.error(
            "Conversation endpoint returned HTTP %s: %s",
            err.response.status_code,
            err.response.text[:300],
        )
        return ConversationResult(
            text=f"Server error (HTTP {err.response.status_code}). Please try again later.",
            response_type="error",
        )
    except httpx.ConnectError as err:
        _LOGGER.error("Cannot connect to conversation endpoint %s: %s", url, err)
        return ConversationResult(
            text="Cannot reach the Lucia server. Check that it is running.",
            response_type="error",
        )

    content_type = response.headers.get("content-type", "")

    if "text/event-stream" in content_type:
        return _parse_sse_response(response)

    # Default: JSON response (instant command path)
    return _parse_json_response(response)


def _parse_json_response(response: httpx.Response) -> ConversationResult:
    """Parse an application/json response from the conversation endpoint."""
    try:
        data = response.json()
    except (json.JSONDecodeError, ValueError) as err:
        _LOGGER.error("Failed to parse JSON response: %s", err)
        return ConversationResult(
            text="Received an invalid response from the server.",
            response_type="error",
        )

    resp_text = data.get("text", "")
    resp_type = data.get("type", "command")
    conv_id = data.get("conversationId")
    needs_input = data.get("needsInput", False)
    command_detail = data.get("commandDetail")

    if not resp_text:
        resp_text = "I processed your request but have no response text."

    return ConversationResult(
        text=resp_text,
        response_type=resp_type,
        conversation_id=conv_id,
        command_detail=command_detail,
        needs_input=needs_input,
    )


def _parse_sse_response(response: httpx.Response) -> ConversationResult:
    """Parse a text/event-stream response, concatenating delta events."""
    collected_text = ""
    final_text: Optional[str] = None
    conv_id: Optional[str] = None
    needs_input = False

    for line in response.text.splitlines():
        if not line.startswith("data:"):
            continue

        raw = line[len("data:"):].strip()
        if not raw or raw == "[DONE]":
            continue

        try:
            event = json.loads(raw)
        except (json.JSONDecodeError, ValueError):
            _LOGGER.debug("Skipping non-JSON SSE line: %s", raw[:100])
            continue

        event_type = event.get("type", "")
        if event_type == "delta":
            collected_text += event.get("text", "")
        elif event_type == "done":
            final_text = event.get("text", collected_text)
            conv_id = event.get("conversationId", conv_id)
            needs_input = event.get("needsInput", False)
        elif event_type == "error":
            error_msg = event.get("message", "Unknown streaming error")
            _LOGGER.error("SSE error event: %s", error_msg)
            return ConversationResult(
                text=f"Streaming error: {error_msg}",
                response_type="error",
            )

        # Capture conversationId from any event that carries it
        if "conversationId" in event and event["conversationId"]:
            conv_id = event["conversationId"]

    result_text = final_text if final_text is not None else collected_text
    if not result_text:
        result_text = "I processed your request but have no response text."

    return ConversationResult(
        text=result_text,
        response_type="llm",
        conversation_id=conv_id,
        needs_input=needs_input,
    )
