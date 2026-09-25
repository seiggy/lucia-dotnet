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

## Voice admission and fast-path questions

`Wyoming:VoiceProfiles:IgnoreUnknownVoices`, shown as **Enrolled voices only**,
is enforced in the conversation processor before either fast-path execution or
LLM fallback. The processor consumes the server-issued voice token and checks
that its identified speaker is still enrolled and authorized. Display-name
tags alone do not establish an enrolled identity. A satellite request without
verified voice metadata is rejected when the option is enabled.

The saved flag follows the configuration provider's reload cycle. Explicit
enrollment and its follow-up turns remain available to unknown speakers.
Turning the option off restores ordinary unknown-speaker routing, and typed
dashboard requests without satellite context remain available in either mode. Rejections create a
redacted command-trace error without copying the speech into that trace.
Existing transcript recording and retention remain unchanged.

Questions and explicit question punctuation do not enter the action-template
fast path. They retain their original text for the agent to interpret, including
the common STT confusion between "are the ... on" and "or the ... on".

When deterministic entity resolution misses, the fast path retries through
embedding search but acts only on a single target: one entity, one area or one
floor. Several fuzzy matches, or a fuzzy match to a `switch` entity, defer to
the agent instead of being switched together.

## Voice onboarding

The Satellite1 path is Satellite1 audio to Home Assistant Assist, then Lucia's
Wyoming STT, then the Lucia custom component's `POST /api/conversation`. It does not
call the dashboard's `/api/onboarding/start` or sample-upload endpoints.

Saying "Learn my voice", "Enroll my voice", "Enroll my voice profile", or
"I want to enroll my voice" starts a deterministic conversation before command
matching or LLM routing. "Onboard me" and "On board me" remain supported, and the
phrases may start with "Lucia" or "Hey Lucia".
Lucia asks for permission to save a voice profile and shared facts,
asks for a preferred name, then asks for an optional birthday including the year.
"Skip this question", "skip", "no thanks", or "I'd rather not say" leaves the
birthday empty. Lucia reads the name and any birthday back for confirmation
before collecting the configured number of voice samples. Other facts and
preferences are left to normal conversations.

The response is JSON:

```json
{
  "type": "onboarding",
  "text": "What name should I call you?",
  "conversationId": "voice-onboarding:abc-123",
  "needsInput": true
}
```

Clients should preserve the returned `conversationId`, keep `deviceId` unchanged,
speak `text`, and reopen the microphone while `needsInput` is true. Lucia keeps
one active enrollment per satellite and resumes it when a caller retains an older
conversation ID or supplies a new one. Follow-ups still require live voice audio;
another satellite cannot advance that enrollment.
Successful enrollment returns `needsInput: false`. "Repeat" repeats the current
prompt; "cancel" discards unfinished enrollment samples and answers. Inactivity
expires the workflow after ten minutes. Restarting the server requires starting
an unfinished conversation again. Onboarding response IDs have a `voice-onboarding:` prefix;
treat them as opaque and always use the latest returned ID. The prefix prevents
an outstanding sample from executing as a home-control command after a restart.
Completion or cancellation restores the original conversation ID.
The Home Assistant integration retains active onboarding mappings for ten minutes
from the latest response; ordinary conversation mappings retain their five-minute TTL.
Its HA-visible ID also carries the onboarding prefix until completion or cancellation.
If the mapping expires or the integration restarts, the prefixed ID is still forwarded,
allowing the server to reject the stale sample instead of treating it as a new command.
Completion or cancellation restores the original HA-visible ID.

Enrollment uses the existing quality checks, `OnboardingSampleCount`,
`MinSampleDurationMs`, and `SpeakerVerificationThreshold`. A mismatched phrase,
quiet or short recording, or voice that differs from earlier samples retries the
current prompt. Sample phrases never execute home-control commands. A speaker
recognition model must be active.
Voice-started enrollment matches the captured voice against existing provisional
profiles and promotes a matching profile through the normal enrollment process.
Unrelated provisional profiles are left unchanged.

### Managing remembered details

Administrators can inspect enrolled users through **User memories** in the
dashboard, or follow **View memories** from a voice profile. Editing or deleting
an entry changes only that profile's stored memory, not its audio recordings.
The page does not infer an author for entries because provenance is not stored.

The existing `GET /api/memory/{userId}` endpoint accepts `personalOnly=true` to
exclude internal chat history before the 200-entry limit, and an optional
`query` of up to 200 characters. Search treats the query as a literal substring
of a key or value on every storage provider, including `%`, `_`, and backslashes.
Administrator sessions may access the selected profile ID; ordinary user sessions
retain their user-ID boundary, and existing trusted service credentials retain
their previous access.

`PUT /api/memory/{userId}/{key}` accepts an optional ISO `expiresAt` timestamp
or `null`, allowing value edits to retain an existing expiration. The store writes
the absolute deadline directly rather than recalculating it from a relative TTL.
It cannot be combined with `ttl` or `ttlSeconds`, and a timestamp must be in the
future and at most 365 days away. `DELETE` removes only the selected key.
If the entry expires or is removed before `PUT` can read it back, the endpoint
returns `400` with a refresh instruction. It does not extend the deadline or retry
the write.

### Enrollment diagnostics

Every handled onboarding turn is recorded in **Cmd Traces** as a local workflow.
The optional `workflow` object includes its name, current stage, returned
conversation ID, and `needsInput` flag. The request context retains the incoming
ID, so changes between turns are visible. Existing command-trace outcomes remain
unchanged; local workflows count as `commandHandled`.

Start phrases remain searchable. Personal replies are replaced with
`[Voice onboarding reply]`, and response text is summarized. Audio and voice-turn
tokens are not copied into command traces. The agent **Traces** view remains for
LLM invocations; enrollment does not manufacture an LLM trace.

On the appliance, `Wyoming__VoiceProfiles__AudioClipBasePath` points to
`/var/lib/lucia/voice-clips`. Enrollment recordings and profile-deletion markers
must use that writable data directory, not the read-only application directory.

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
`preferred_name` and, when shared, `birthday`. Birthdays retain the wording the
speaker confirmed. Onboarding does not infer missing date components, calculate
ages, or create reminders. A skipped birthday is not stored.
Existing room and preference memories are preserved and remain editable in the
dashboard. Already-enrolled users can ask Lucia to remember their birthday
without enrolling again. Memories use the existing `IMemoryStore` and configured MongoDB,
SQLite, or PostgreSQL provider, with the existing in-memory fallback when no
durable provider is registered.

The agent context provider loads memories for the detected enrolled user on each
invocation. Reconstructed requests intentionally include stored memories and recent
history so telemetry captures the context used for orchestration and debugging.
Treat these logs and traces as sensitive and apply appropriate access and retention
controls. The `memory_read`, `memory_search`, `memory_remember`, and
`memory_forget` tools are bound to that user; the model cannot supply a different
user's ID. The agent interprets the speaker's natural-language intent and supplies
`explicitlyRequested` when saving or forgetting a memory. The flag is a behavioral
guard, not a hard authorization boundary; this voice-first flow intentionally does
not add another authentication or confirmation turn. Correct flag selection is a
model-behavior requirement to cover with evaluations, while profile isolation
remains server-enforced. The existing authenticated
`/api/memory/{userId}` endpoints
can also inspect or change memories using the stable speaker profile ID.
The User memories dashboard uses these same endpoints.

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
Personal-memory searches exclude the reserved `chat_history` namespace in the
datastore before applying the result limit. Recent conversation turns therefore
cannot crowd saved preferences out of memory tools or injected user context;
history retrieval continues to use the unfiltered memory search.

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
