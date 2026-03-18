# Conversation API Reference

The `/api/conversation` endpoint provides a streamlined command processing pipeline for Lucia. Pattern-matched commands (lights, climate, scenes) execute directly against Home Assistant without invoking an LLM. Unrecognized requests fall back to LLM orchestration with SSE streaming.

## POST /api/conversation

### Request

```json
{
  "text": "turn on the kitchen lights",
  "context": {
    "timestamp": "2026-03-18T16:47:14Z",
    "conversationId": "abc-123",
    "deviceId": "satellite_kitchen",
    "deviceArea": "kitchen",
    "deviceType": "voice_assistant",
    "userId": "zack",
    "location": "Home"
  },
  "promptOverride": null
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `text` | string | Yes | The user's natural-language input |
| `context.timestamp` | ISO 8601 | No | When the request originated |
| `context.conversationId` | string | No | Tracks a multi-turn conversation |
| `context.deviceId` | string | No | Originating device identifier |
| `context.deviceArea` | string | No | Area hint for entity disambiguation |
| `context.deviceType` | string | No | `voice_assistant`, `dashboard`, etc. |
| `context.userId` | string | No | Authenticated user identifier |
| `context.location` | string | No | Logical location (e.g. `"Home"`) |
| `promptOverride` | string | No | Replace the default system prompt for LLM fallback |

### Response — Command Parsed (JSON)

When the parser matches a known command, the response is immediate JSON:

```http
HTTP/1.1 200 OK
Content-Type: application/json
```

```json
{
  "type": "command",
  "text": "OK, I turned on the kitchen lights.",
  "command": {
    "skillId": "LightControlSkill",
    "action": "toggle",
    "confidence": 0.95,
    "captures": { "action": "on", "entity": "kitchen lights" },
    "executionMs": 42
  },
  "conversationId": "abc-123"
}
```

| Field | Description |
|-------|-------------|
| `type` | Always `"command"` for parsed commands |
| `text` | Human-friendly confirmation rendered from a response template |
| `command.skillId` | Which skill handled the request |
| `command.action` | Skill-specific action that was executed |
| `command.confidence` | Parser confidence score (0–1) |
| `command.captures` | Named regex captures from the input text |
| `command.executionMs` | Wall-clock time for HA service call |
| `conversationId` | Echo of the request conversation ID |

### Response — LLM Fallback (SSE)

When no command pattern matches, the endpoint streams Server-Sent Events:

```http
HTTP/1.1 200 OK
Content-Type: text/event-stream
```

```
event: metadata
data: {"type":"llm","conversationId":"abc-123"}

event: done
data: {"text":"The weather is 72°F and sunny.","conversationId":"abc-123","needsInput":false}
```

| Event | Description |
|-------|-------------|
| `metadata` | Sent first. Contains `type: "llm"` and the conversation ID |
| `done` | Final event with the complete response text and `needsInput` flag |
| `error` | Sent if the LLM pipeline fails: `{"error":"Error message"}` |

## Response Template API

**Base path:** `/api/response-templates`

Templates control how parsed-command confirmations are rendered (e.g. *"OK, I turned on the {entity}."*).

| Method | Path | Description |
|--------|------|-------------|
| GET | `/` | List all templates |
| GET | `/{id}` | Get a single template |
| POST | `/` | Create a new template |
| PUT | `/{id}` | Update an existing template |
| DELETE | `/{id}` | Delete a template |
| POST | `/reset` | Reset all templates to built-in defaults |

## Supported Commands

| Skill | Action | Example Phrases |
|-------|--------|----------------|
| LightControlSkill | toggle | "turn on the kitchen lights", "lights off in bedroom" |
| LightControlSkill | brightness | "set living room brightness to 50 percent" |
| ClimateControlSkill | set_temperature | "set thermostat to 72 degrees" |
| ClimateControlSkill | adjust | "make it warmer", "make it cooler in the bedroom" |
| SceneControlSkill | activate | "activate the movie scene" |

Commands are matched by regex patterns registered per skill. The `deviceArea` context field is used to disambiguate entities when the utterance doesn't include an explicit area.

## Telemetry

All metrics are emitted via OpenTelemetry under the `lucia.conversation` meter.

| Metric | Type | Tags | Description |
|--------|------|------|-------------|
| `conversation.command_parsed` | Counter | `skillId`, `action` | Incremented for each successfully parsed command |
| `conversation.llm_fallback` | Counter | — | Incremented when the request falls through to LLM |
| `conversation.command_parsed.duration_ms` | Histogram | `skillId`, `action` | End-to-end latency for command parse + HA execution |
| `conversation.llm_fallback.duration_ms` | Histogram | — | End-to-end latency for LLM orchestration |
