# Home Assistant Integration Updates

> Historical context: Entries below are kept as originally written. Any "coming soon" or "next steps" language in older notes reflects that point in time, not necessarily current capability. For current behavior and setup, use `custom_components/lucia/README.md`.


# Release Notes - 2026.02.27

**Release Date:** February 27, 2026

## Changes: Agent catalog discovery improvements / Clarification context and voice

- **Conversation context preservation** — When Lucia asks for clarification (e.g. “Which light do you want?”), the previous assistant response is now stored and sent with the next user message. Follow-ups like “turn them all on” are interpreted in context so the agent can resolve “them” to the options just offered.
- **Voice / input-required signaling** — When the agent returns a clarification-style response (including when the reply ends with “?”), the integration returns `continue_conversation=True` so the voice pipeline stays open for the user’s reply. Optional event `lucia_conversation_input_required` is fired so automations or custom clients can react (e.g. trigger the assist pipeline to listen again).
- **AssistantContent/ChatLog compatibility** — Conversation platform now works on Home Assistant versions before 2026.1. When `AssistantContent` or `ChatLog` are not available (older HA), chat log updates are skipped; responses still work normally.
- **async_process implementation** — Implements abstract `async_process` for older HA where `ConversationEntity` requires it; uses real chat log when helpers exist (HA 2026.1+), no-op otherwise.
- **401 error handling** — When the Lucia `/agents` endpoint returns 401 Unauthorized, the integration now surfaces "Authentication failed (401). Check that the API key is correct and not revoked." instead of the generic "Invalid agent catalog"
- **Catalog format support** — Accepts `value` key in addition to `agents` and `catalog` when parsing the agent catalog response for broader compatibility
- **Lucia catalog now anonymous** — Lucia's GET `/agents` endpoint allows anonymous access, so the integration can fetch the catalog during setup without requiring an API key (Lucia 2026.02.27+)


# Release Notes - 2025.11.09

**Release Date:** November 9, 2025  
**Code Name:** "Constellation"

---

## 🌌 Overview

"Constellation" delivers the feature we've been building toward all year: multi-agent orchestration working end-to-end inside Lucia. Requests can now fan out to the most relevant specialists, combine their output, and respond with a natural narrative backed by contextual awareness. Alongside the orchestration milestone, we introduce a new general-knowledge agent that fills in the gaps when a domain specialist is unavailable, plus targeted refinements to the lighting and music skills that make everyday interactions smoother.

## 🚀 Highlights

- **Multi-Agent Orchestration (GA)** — Router, dispatch, and aggregator executors now coordinate multiple agents in a single workflow, with task persistence and telemetry baked in. Complex requests like "Dim the kitchen lights and play relaxing jazz" are handled as one coherent conversation.
- **General Knowledge Agent** — A new catalog entry that handles open-ended queries, status questions, and conversation handoffs when no specialist is a clean match. It plugs directly into the orchestrator so fallbacks feel intentional instead of abrupt.
- **Smarter Light Selection** — Improved semantic matching, room disambiguation, and capability detection make it far easier to target the right fixture on the first try—even when users describe locations conversationally.
- **Music Skill Enhancements** — Faster player discovery, richer queue summaries, and better error messaging tighten the loop between Music Assistant and Lucia’s orchestration pipeline.

## 🔧 Under the Hood

- Expanded orchestration telemetry with detailed WorkflowErrorEvent parsing and OpenTelemetry spans for traceability.
- Options flow updated to align with Home Assistant 2025.12 requirements (no more manual `self.config_entry`).
- HTTP client instrumentation now captures request/response headers and payloads when traces are recorded, aiding diagnostics of A2A traffic.

## ✅ Upgrade Notes

- No breaking schema changes, but existing installations should reload the integration after updating to register the new general agent card.
- Home Assistant users will no longer see the 2025.12 config-flow deprecation warning.


> Date: October 7, 2025
> Changes: Fixed JSON-RPC communication and agent catalog fetching

## Summary

Updated the Lucia Home Assistant integration to use the corrected JSON-RPC format without `taskId` and implemented proper agent catalog discovery.

## Key Changes

### 1. conversation.py - Fixed Message Protocol

**Removed Dependencies:**
- Removed `a2a.client` imports (ClientConfig, ClientFactory)
- Removed `a2a.types` imports (Message, Part, Role, TextPart)
- Now uses direct HTTP communication with `httpx`

**Updated `async_process()` Method:**
- Switched from A2A SDK to direct JSON-RPC 2.0 format
- **Critical Fix**: Set `taskId: null` (Agent Framework doesn't support task management)
- Uses `contextId` for conversation threading (maps to Home Assistant's `conversation_id`)
- Sends JSON-RPC request with `method: "message/send"`
- Properly handles JSON-RPC response structure with `result` wrapper

**Message Structure:**
```python
{
    "jsonrpc": "2.0",
    "method": "message/send",
    "params": {
        "message": {
            "kind": "message",
            "role": "user",
            "parts": [{
                "kind": "text",
                "text": "...",
                "metadata": None
            }],
            "messageId": "<uuid>",
            "contextId": "<conversation_id>",
            "taskId": None,  # ← KEY FIX
            "metadata": None,
            "referenceTaskIds": [],
            "extensions": []
        }
    },
    "id": 1
}
```

**Response Parsing:**
- Extracts text from `result.parts[].text`
- Handles JSON-RPC error responses
- Validates status codes properly

### 2. __init__.py - Agent Catalog Discovery

**Removed Dependencies:**
- Removed `a2a.client` imports (A2ACardResolver, ClientConfig, ClientFactory)
- Removed `a2a.types` imports (AgentCard)
- Now uses direct HTTP for catalog fetching

**Updated `async_setup_entry()` Function:**
- Fetches agent catalog from `/agents` endpoint
- Validates catalog response (must be list of agent objects)
- Extracts first agent from catalog (TODO: add agent selection UI)
- Converts relative URLs to absolute (prepends repository URL)
- Stores full catalog for future multi-agent selection

**Stored Data Structure:**
```python
hass.data[DOMAIN][entry.entry_id] = {
    "httpx_client": httpx_client,      # HTTP client with auth headers
    "agent_card": agent_card,           # Selected agent's card data
    "agent_url": agent_url,             # Full agent URL (e.g., https://localhost:7235/a2a/light-agent)
    "catalog": agents,                  # Full agent catalog array
    "repository": repository,           # Repository base URL
}
```

**Connection Flow:**
1. Create httpx.AsyncClient with optional X-Api-Key header
2. Fetch `{repository}/agents` endpoint
3. Parse JSON array of agent cards
4. Select first agent (default behavior)
5. Build absolute agent URL from relative path
6. Store all data in `hass.data[DOMAIN]`

**Configuration:**
- Made API key optional (not required for localhost testing)
- Added `verify=False` for self-signed SSL certificates (development)
- Added timeout configuration (30 seconds)

## Testing Results

Both message protocols tested successfully:

### JSON-RPC Format ✓
- Endpoint: `{agent_url}` (base agent URL)
- Method: `message/send`
- Status: 200 OK
- Response: Wrapped in `result` field
- Context threading: Working

### OpenAPI Format ✓
- Endpoint: `{agent_url}/v1/message:send`
- Status: 200 OK
- Response: Direct message object
- Context threading: Working

**Recommendation**: Both formats work, but JSON-RPC is now implemented as it's more aligned with the agent framework's RPC-style architecture.

## Breaking Changes

None for users, but developers should note:
- No longer depends on A2A Python SDK
- Uses direct HTTP/JSON-RPC communication
- Agent catalog must be available at `/agents` endpoint

## Next Steps

1. **Agent Selection UI** - Add config flow options to let users select which agent to use
2. **Multi-Agent Support** - Allow switching between different agents (light-agent, music-agent, etc.)
3. **Catalog Refresh** - Add periodic catalog updates to discover new agents
4. **Error Handling** - Improve error messages for common failure scenarios
5. **SSL Configuration** - Add option to verify SSL certificates in production

## Migration Notes

No migration needed for existing installations. The integration will:
1. Fetch the agent catalog on startup
2. Automatically select the first available agent
3. Continue working with existing conversations

Users who want to switch agents will need to wait for the agent selection UI (coming soon).
