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

## Voice onboarding

The Satellite1 path is Satellite1 audio to Home Assistant Assist, then Lucia's
Wyoming STT, then the Lucia custom component's `POST /api/conversation`. It does not
call the dashboard's `/api/onboarding/start` or sample-upload endpoints.

Saying "Onboard me" starts a deterministic conversation before command matching or
LLM routing. Lucia asks for permission to save a voice profile and shared facts,
asks for a preferred name, then asks optional room and interaction preferences.
"Skip" leaves either optional answer empty. Lucia reads the answers back for
confirmation before collecting the configured number of voice samples.

The response is JSON:

```json
{
  "type": "onboarding",
  "text": "What name should I call you?",
  "conversationId": "voice-onboarding:abc-123",
  "needsInput": true
}
```

Home Assistant must preserve `conversationId` and `deviceId`, speak `text`, and
reopen the microphone while `needsInput` is true. The Lucia custom component does
this, including generating or adopting the first Home Assistant conversation ID.
Successful enrollment returns `needsInput: false`. "Repeat" repeats the current
prompt; "cancel" discards unfinished enrollment samples and answers. Inactivity
expires the workflow after ten minutes. Restarting the server requires starting
an unfinished conversation again. Onboarding response IDs have a `voice-onboarding:` prefix;
treat them as opaque and always use the latest returned ID. The prefix prevents
an outstanding sample from executing as a home-control command after a restart.
Completion or cancellation restores the original conversation ID.

Enrollment uses the existing quality checks, `OnboardingSampleCount`,
`MinSampleDurationMs`, and `SpeakerVerificationThreshold`. A mismatched phrase,
quiet or short recording, or voice that differs from earlier samples retries the
current prompt. Sample phrases never execute home-control commands. A speaker
recognition model must be active.

### Audio handoff and identity

Wyoming prefixes each transcript with an opaque, single-use
`<lucia-voice token="..." />` tag. The component forwards this text unchanged.
The API consumes the associated server-side utterance and uses its original
transcript, audio, sample rate, and detected speaker. Tokens expire after two
minutes; expired or replayed tokens request another voice turn instead of
executing the text. The bounded handoff cache holds at most 32 MiB in process.
Wyoming STT and the conversation API must reach the same AgentHost instance.

The API resolves the detected profile ID against the current speaker store.
Only an authorized, non-provisional profile supplies personal memory context.
Legacy `<Name />` tags remain display hints and never authorize memory access.
Unknown voice turns do not use the Home Assistant service account as a substitute
identity. Orchestrator history is also partitioned by detected profile, so a
speaker change within one satellite conversation does not replay another user's
personal context.

Voice matching is probabilistic personalization, not an authentication mechanism
for sensitive data or privileged actions.

### Personal memories

Confirmed onboarding facts are stored under the enrolled profile ID as
`preferred_name`, `preferred_room`, and `preferences`. Omitted optional answers
are not stored. Memories use the existing `IMemoryStore` and configured MongoDB,
SQLite, or PostgreSQL provider, with the existing in-memory fallback when no
durable provider is registered.

The agent context provider loads memories for the detected enrolled user on each
invocation. The `memory_read`, `memory_search`, `memory_remember`, and
`memory_forget` tools are bound to that user; the model cannot supply a different
user's ID. Writes require an explicit user request. The existing authenticated
`/api/memory/{userId}` endpoints
can also inspect or change memories using the stable speaker profile ID.
There is no new memory-management dashboard in this change.

Memory tools are registered on in-process `ChatClientAgent` agents, including
the built-in, dynamic, music, and timer agents. Remote A2A services and agents
configured with Lucia's experimental GitHub Copilot provider do not receive
these memory tools.

Stored memories are untrusted data, not instructions. Use them for preferences
and facts the user chooses to share, not credentials. If a cloud model is
configured, memory included in its prompt is sent to that model like other
conversation context. Existing auto-profiling and transcript-retention settings
are unchanged by onboarding consent. Forgetting a saved memory removes that
entry, not existing conversation history or transcript records.

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

All metrics are emitted via OpenTelemetry under the `lucia.AgentHost` meter.

| Metric | Type | Tags | Description |
|--------|------|------|-------------|
| `conversation.command_parsed` | Counter | `skillId`, `action` | Incremented for each successfully parsed command |
| `conversation.llm_fallback` | Counter | — | Incremented when the request falls through to LLM |
| `conversation.command_parsed.errors` | Counter | `skillId`, `action` | Incremented when a command execution fails |
| `conversation.command_parsed.duration_ms` | Histogram | — | End-to-end latency for command parse + HA execution |
| `conversation.llm_fallback.duration_ms` | Histogram | — | End-to-end latency for LLM orchestration |
