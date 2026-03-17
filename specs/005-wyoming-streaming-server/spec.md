# Feature Specification: Wyoming Streaming Voice Server

**Feature Branch**: `feat-wyoming-server`
**Created**: 2026-03-13
**Status**: Draft
**Input**: User description: "Add full Wyoming protocol support for both streaming STT, TTS, and streaming wake-word using sherpa-onnx for STT / wake word streaming, and ONNX runs of Qwen3-TTS and Chatterbox Turbo for TTS over Wyoming. Build a diarization engine to short-cut the LLM for faster command processing when possible, then hand off to the LLM orchestrator when it's not."

## Overview

Lucia currently integrates with Home Assistant's voice pipeline as a text-based conversation agent. This feature adds a native Wyoming protocol server implemented as a `lucia.Wyoming` class library and hosted in-process inside `lucia.AgentHost`, enabling Lucia to directly handle streaming audio from Wyoming satellites and voice pipelines — performing speech-to-text, wake word detection, text-to-speech, and speaker verification locally using ONNX-based models. Phase 1 bridges Wyoming requests into Lucia's existing text-based API, while Phase 2 explicitly extends the orchestrator contract for metadata-rich voice-native invocation rather than assuming the current text API is sufficient. This in-process architecture simplifies deployment, configuration, and maintenance for users by keeping the Wyoming TCP listener and existing HTTP API in one process, one container, and one shared configuration surface. A speaker-verification engine (referred to in some phase documents as diarization) provides fast-path command routing that bypasses the LLM when transcribed speech matches known command patterns, dramatically reducing latency for common operations.

### Phased Delivery

| Phase | Scope | Key Deliverable |
|-------|-------|----------------|
| **Phase 1** | Wyoming Protocol + STT + Wake Word + Device Pairing | Working Wyoming server with streaming STT, wake word, and paired-device enforcement via sherpa-onnx |
| **Phase 2** | Speaker Verification & Command Routing | Speaker-aware command shortcutting plus metadata-rich orchestrator invocation |
| **Phase 3** | TTS Synthesis | Qwen3-TTS and Chatterbox Turbo via direct ONNX Runtime over Wyoming |
| **Phase 4** | Integration & Polish | End-to-end voice pipeline, multi-satellite, performance tuning |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Wyoming STT Integration (Priority: P0)

A Home Assistant user with a Wyoming-compatible satellite (e.g., Raspberry Pi with microphone) wants Lucia to transcribe spoken commands locally using streaming speech-to-text, without relying on cloud services like Whisper or Google STT.

**Why this priority**: This is the foundational capability. Without a working Wyoming server that can receive audio and produce transcriptions, no other voice feature can function. Streaming STT with sherpa-onnx provides the core audio→text pipeline.

**Independent Test**: Start the Wyoming server, connect a test client sending WAV/PCM audio chunks via the Wyoming protocol, and verify that a transcript event is returned with accurate text within acceptable latency (< 500ms after final audio chunk).

**Acceptance Scenarios**:

1. **Given** the Wyoming server is running and advertising STT capability via `describe`/`info` events, **When** a Wyoming client sends `audio-start`, multiple `audio-chunk` events with 16kHz 16-bit PCM audio, and `audio-stop`, **Then** the server responds with a `transcript` event containing recognized text
2. **Given** streaming audio from a satellite microphone, **When** audio is sent in real-time 20ms chunks, **Then** the server processes chunks incrementally and returns partial results (streaming recognition) with final transcript after `audio-stop`
3. **Given** the server is configured with the sherpa-onnx Zipformer streaming model, **When** a 5-second English utterance is transcribed, **Then** the end-to-end latency from `audio-stop` to `transcript` is under 500ms on a modern x86_64 CPU
4. **Given** the Wyoming TCP listener is hosted inside `lucia.AgentHost`, **When** `lucia.AgentHost` starts, **Then** the Wyoming TCP listener starts alongside the AgentHost HTTP API, is configured via the same `appsettings.json`, and is discoverable via Zeroconf/mDNS
5. **Given** an invalid or corrupted audio stream, **When** audio chunks contain non-PCM data or unexpected sample rates, **Then** the server responds with an `error` event and does not crash

---

### User Story 2 — Streaming Wake Word Detection (Priority: P0)

A user wants their Wyoming satellite to continuously listen for a wake word (e.g., "Hey Lucia") and only begin transcription when the wake word is detected, enabling hands-free activation.

**Why this priority**: Wake word detection is co-equal with STT as a foundational requirement. Without it, the satellite cannot autonomously trigger voice sessions. sherpa-onnx provides keyword spotting that integrates naturally with the STT pipeline.

**Independent Test**: Stream continuous audio containing a wake word phrase to the Wyoming server's wake word endpoint. Verify that a `detection` event fires within 300ms of the wake word being spoken, and that no false positives occur during 60 seconds of non-wake-word speech.

**Acceptance Scenarios**:

1. **Given** the Wyoming server is running with wake word detection enabled, **When** a Wyoming client sends `detect` followed by continuous `audio-chunk` events, **Then** the server monitors for the configured wake word(s)
2. **Given** continuous audio containing the wake word "Hey Lucia", **When** the wake phrase is spoken, **Then** the server emits a `detection` event with the wake word name and timestamp within 300ms
3. **Given** 60 seconds of continuous non-wake-word conversational speech, **When** no wake word is spoken, **Then** no `detection` event fires (false positive rate < 1 per hour)
4. **Given** a noisy environment with background music or TV audio, **When** the wake word is spoken clearly, **Then** detection still occurs reliably (> 95% recall in moderate noise)
5. **Given** wake word detection triggers, **When** the detection event fires, **Then** the server automatically transitions to STT mode for the subsequent utterance without requiring client-side coordination

---

### User Story 3 — Speaker-Verification-Based Command Routing (Priority: P1)

A user issues common home automation commands (e.g., "turn off the kitchen lights", "set thermostat to 72") and expects near-instant execution without the latency of a full LLM round-trip. In this phase, "diarization" means single-utterance speaker verification rather than multi-speaker segmentation: the engine identifies which enrolled speaker most closely matches the utterance and routes recognized command patterns directly to the appropriate agent/skill, falling back to the LLM orchestrator for complex or ambiguous requests.

**Why this priority**: Reducing command latency from 2-5 seconds (LLM round-trip) to under 500ms for common commands is a transformative UX improvement. This is the key differentiator that makes local voice competitive with cloud assistants.

**Independent Test**: Send transcribed text matching known command patterns through the speaker-verification routing engine. Verify that pattern-matched commands execute in under 200ms without LLM involvement, and that ambiguous commands are correctly routed to the LLM orchestrator.

**Acceptance Scenarios**:

1. **Given** a transcription "turn off the living room lights", **When** the speaker-verification routing engine processes the text, **Then** it pattern-matches to `LightControlSkill` with entity "living room lights" and action "off", executing without LLM involvement in < 200ms
2. **Given** a transcription "what's the weather like tomorrow and also remind me to buy groceries", **When** the speaker-verification routing engine cannot confidently match a single command pattern, **Then** it hands off to the LLM orchestrator for full processing
3. **Given** multiple household members speaking to the same satellite, **When** speaker embeddings differ from the enrolled primary user, **Then** the speaker-verification routing engine can optionally restrict command execution to authorized speakers
4. **Given** a command that partially matches multiple skills, **When** confidence is below the routing threshold, **Then** the engine falls back to the LLM with the transcription plus speaker verification context (speaker ID, confidence scores) as enrichment
5. **Given** the speaker-verification routing engine is processing commands, **When** telemetry is emitted, **Then** it includes routing decision (fast-path vs LLM), confidence score, matched skill, and latency metrics

---

### User Story 4 — Text-to-Speech via Wyoming (Priority: P2)

A user expects Lucia's responses to be spoken aloud through their Wyoming satellite's speaker, using high-quality local TTS models (Qwen3-TTS or Chatterbox Turbo) instead of cloud-based synthesis.

**Why this priority**: TTS completes the voice loop but is not required for the core command pipeline. Users can receive text responses via the HA UI in the interim. Local TTS with voice cloning capability adds significant value for personalization.

**Independent Test**: Send a `synthesize` event with text to the Wyoming server. Verify that it returns streaming `audio-start`, `audio-chunk`, `audio-stop` events with synthesized PCM audio that is intelligible and natural-sounding.

**Acceptance Scenarios**:

1. **Given** the Wyoming server is running with TTS enabled (Qwen3-TTS model loaded), **When** a `synthesize` event is received with text "The kitchen lights have been turned off", **Then** the server responds with `audio-start`, streaming `audio-chunk` events containing 24kHz PCM audio, and `audio-stop`
2. **Given** the Chatterbox Turbo model is configured as an alternative TTS engine, **When** a `synthesize` event specifies the Chatterbox voice, **Then** synthesis completes with sub-200ms time-to-first-audio for short phrases
3. **Given** a text response longer than 200 characters, **When** TTS synthesis runs, **Then** audio chunks begin streaming before the entire text is fully synthesized (streaming/chunked synthesis)
4. **Given** a reference voice sample has been enrolled for voice cloning, **When** TTS synthesis uses the cloned voice, **Then** the output audio resembles the reference speaker's voice characteristics
5. **Given** the TTS service receives multiple concurrent synthesis requests, **When** requests arrive from different satellites, **Then** synthesis is queued and processed without blocking the STT pipeline

---

### User Story 5 — End-to-End Voice Pipeline (Priority: P2)

A user speaks a command to their satellite, and the full pipeline — wake word → STT → speaker verification/routing → agent execution → TTS response — executes seamlessly inside `lucia.AgentHost` with in-process handoffs between the Wyoming listener and Lucia orchestration components, with minimal perceivable latency.

**Why this priority**: This is the integration story that ties all phases together. Individual components must work before the full pipeline can be validated.

**Independent Test**: Speak a command to a Wyoming satellite. Measure end-to-end latency from end-of-speech to beginning-of-TTS-response. For fast-path commands, target < 1 second; for LLM-routed commands, target < 4 seconds.

**Acceptance Scenarios**:

1. **Given** a fully configured Wyoming voice pipeline, **When** the user says "Hey Lucia, turn on the bedroom lights", **Then** the wake word triggers, STT transcribes the command, the speaker-verification engine fast-paths it to LightControlSkill, and a TTS confirmation plays — all within 1.5 seconds end-to-end
2. **Given** the user asks a complex question requiring LLM reasoning, **When** STT transcribes and speaker verification routes to the orchestrator, **Then** the LLM response is synthesized and played back within 5 seconds
3. **Given** the `continue_conversation` flag is set by the agent, **When** the TTS response finishes playing, **Then** the satellite automatically returns to STT listening mode for follow-up input
4. **Given** multiple satellites in different rooms, **When** each satellite maintains an independent Wyoming session, **Then** commands from different rooms are processed concurrently without interference
5. **Given** the full pipeline is running, **When** OpenTelemetry traces are examined, **Then** each stage (wake → STT → route → execute → TTS) is instrumented with spans showing timing breakdown

---

### User Story 6 — Voice Profile Management & Speaker Filtering (Priority: P1)

A household with multiple members and ambient audio sources (TV, music, podcasts) needs the system to intelligently manage speaker identities. The system automatically discovers and tracks unknown voices, provides a guided onboarding flow for household members to train their voiceprint, and supports an "ignore unknown voices" mode that filters out commands from unrecognized speakers — effectively ignoring TV dialogue, music lyrics, and other non-human-directed speech.

**Why this priority**: Speaker identification directly impacts the speaker-verification engine's effectiveness (Phase 2). Without robust voice profile management, the system can't distinguish household members from background audio, leading to false command triggers from TV shows or music. Automated discovery reduces setup friction while the ignore-unknown mode is essential for noisy households.

**Independent Test**: Play a TV show through speakers near the satellite while a registered user gives commands. With "ignore unknown voices" enabled, verify that TV dialogue does not trigger command processing while the registered user's commands execute normally.

**Acceptance Scenarios**:

1. **Given** a new speaker speaks a command for the first time, **When** the speaker-verification engine detects an unrecognized voice embedding, **Then** the system automatically creates a provisional "unknown-speaker-N" profile, stores the embedding, and logs the event — the command is still processed unless "ignore unknown voices" is enabled
2. **Given** the same unknown speaker issues multiple commands over time, **When** the system recognizes consistent embeddings matching the provisional profile, **Then** it consolidates observations and surfaces a prompt (via dashboard or TTS) suggesting the user complete voice enrollment: "I've noticed a new voice. Would you like to set up a voice profile?"
3. **Given** a user initiates voice onboarding (via dashboard UI or voice command "Hey Lucia, set up my voice"), **When** the onboarding flow starts, **Then** the system guides the user through 5-10 spoken prompts (e.g., "Please say: 'Turn on the living room lights'", "Please say: 'What's the weather today?'") to collect diverse speech samples, extracts multiple embeddings, computes an averaged profile, and confirms enrollment with "Voice profile created for [name]. I'll recognize your voice from now on."
4. **Given** "ignore unknown voices" is enabled in configuration, **When** an unrecognized speaker (not matching any enrolled profile above the similarity threshold) issues a command, **Then** the system silently discards the transcription without processing, routing, or responding — but still logs the event for diagnostics
5. **Given** "ignore unknown voices" is enabled, **When** an enrolled household member speaks a command, **Then** the command is processed normally with full fast-path and LLM routing capabilities
6. **Given** TV audio or music is playing near the satellite, **When** "ignore unknown voices" is enabled and no enrolled speaker's voice is detected, **Then** false wake word triggers from TV audio are suppressed after the speaker verification step, preventing phantom command execution
7. **Given** the onboarding flow is in progress, **When** the environment is too noisy or the speech samples are too short/quiet, **Then** the system provides feedback ("I couldn't hear that clearly, please try again in a quieter spot") and allows retry without restarting the entire flow

### User Story 7 — Custom Wake Words & Browser-Based Onboarding (Priority: P1)

After completing Lucia's core setup, a user wants to choose their own wake word (e.g., "Hey Jarvis", "Computer", "OK Home") instead of the default "Hey Lucia", and wants a simple browser-based onboarding experience accessible from their phone, tablet, or laptop through the configured dashboard. The system uses sherpa-onnx's open-vocabulary keyword spotting — no model training required — and offers optional voice calibration to tune detection sensitivity for the user's specific voice and environment.

**Why this priority**: Wake word personalization is a key differentiator for a privacy-first local assistant. The browser-based onboarding flow dramatically lowers the barrier to entry for non-technical users once Lucia itself is fully configured and reachable.

**Independent Test**: Open the authenticated onboarding page from the configured Lucia dashboard on a phone browser, create a custom wake word "OK Computer", optionally record 3 calibration samples, and verify the Wyoming server immediately responds to the new wake phrase from any paired satellite.

**Acceptance Scenarios**:

1. **Given** the user has completed Lucia setup and navigates to the dashboard onboarding page on any device with a microphone (phone, tablet, laptop), **When** they enter a custom wake word phrase (e.g., "Hey Jarvis"), **Then** the system tokenizes the phrase using sherpa-onnx's text2token, generates an updated keywords configuration, and the wake word detector begins responding to the new phrase within 5 seconds — zero audio recordings required
2. **Given** a custom wake word has been configured, **When** the user opts into the calibration step, **Then** the browser page prompts them to say the wake word 3-5 times using their device microphone, the system records each sample via the Web Audio API, uploads the audio to the server, measures detection confidence per sample, and auto-tunes the boost score and detection threshold for optimal sensitivity
3. **Given** the browser-based onboarding page inside a configured Lucia deployment, **When** the user accesses it on a mobile phone, **Then** the page is fully responsive, uses the device's built-in microphone via `getUserMedia()`, provides real-time audio level feedback (visual meter), and works in Chrome, Safari, and Firefox without plugins
4. **Given** the calibration recordings, **When** some samples fail to trigger detection (e.g., spoke too quietly, background noise), **Then** the system provides specific feedback ("Try speaking a bit louder" or "I detected your wake word on 4/5 tries — detection sensitivity set to high") and allows retry
5. **Given** multiple household members, **When** each member configures their own wake word through the browser onboarding, **Then** the system supports multiple simultaneous wake words with independent per-user calibration thresholds, and identifies which user's wake word was triggered via speaker verification
6. **Given** the combined onboarding flow and a fully configured Lucia deployment, **When** a new user starts setup from the browser, **Then** they can complete both voice profile enrollment (5 spoken prompts) AND custom wake word calibration (3-5 samples) in a single guided session under 3 minutes

---

### Edge Cases

- What happens when the Wyoming TCP connection drops mid-audio-stream? (Server should clean up session state and release STT resources within 5 seconds)
- How does the system handle simultaneous wake word detections from multiple satellites? (Each satellite session is independent; concurrent STT sessions are supported up to configured limit)
- What happens when sherpa-onnx model files are missing or corrupted? (Server should fail health checks and report clear error via Aspire dashboard)
- How does the speaker-verification routing engine handle homophones or STT artifacts? (Fuzzy matching with configurable confidence thresholds; existing `StringSimilarity` utilities can be leveraged)
- What happens when TTS model download fails on first run? (Model download should be a separate initialization step with retry; server should report degraded capability via Wyoming `info`)
- How does the system handle audio formats other than 16kHz 16-bit PCM? (Server should support format negotiation via `audio-start` parameters and resample if needed)
- What happens when GPU memory is exhausted during concurrent TTS synthesis? (Queue with backpressure; degrade to CPU inference with warning)
- What happens when a provisional unknown speaker profile accumulates but is never enrolled? (Auto-expire provisional profiles after 30 days of inactivity; configurable retention)
- How does "ignore unknown voices" interact with wake word detection? (Wake word detection still runs for all audio; speaker verification happens after wake + STT, before command routing)
- How does the system handle wake words that are common English phrases (e.g., "okay")? (Warn user about high false-positive risk; suggest longer phrases of 2-4 syllables minimum)
- What happens if two users configure the same wake word? (Both trigger; speaker verification after wake determines which user profile to use)
- How does the browser recording work over HTTPS? (Microphone access requires secure context; the dashboard already serves over HTTPS via Aspire)
- What if the browser recording page is accessed from outside the local network? (Recording still works but uploaded audio goes to the AgentHost API; standard auth protects the endpoints)
- What happens if an enrolled user's voice changes over time (cold, aging)? (System can optionally update embeddings incrementally from verified high-confidence matches; configurable via "adaptive profiles" setting)
- How does onboarding work with multiple satellites? (Voice profiles are stored centrally in MongoDB; any satellite can be used for enrollment and all satellites share profiles)

## Requirements *(mandatory)*

### Functional Requirements

#### Phase 1 — Wyoming Core + STT + Wake Word

- **FR-001**: System MUST implement the Wyoming protocol TCP server, supporting newline-delimited JSON headers with optional binary payloads per the OHF-Voice/wyoming specification
- **FR-002**: System MUST handle Wyoming event types: `describe`, `info`, `audio-start`, `audio-chunk`, `audio-stop`, `transcribe`, `transcript`, `detect`, `detection`, `not-detected`, `error`
- **FR-003**: System MUST integrate sherpa-onnx streaming ASR (online recognition) for real-time speech-to-text using Zipformer or equivalent streaming transducer models
- **FR-004**: System MUST integrate sherpa-onnx keyword spotter for configurable wake word detection with custom keyword model support
- **FR-005**: System MUST support concurrent Wyoming client connections with independent session state per connection
- **FR-006**: System MUST be implemented as a `lucia.Wyoming` class library, hosted in-process by `lucia.AgentHost` as an `IHostedService`, with the Wyoming TCP port exposed alongside the existing HTTP API
- **FR-006a**: System MUST require device pairing in Phase 1: paired device tokens are managed from the dashboard, `WyomingSession` MUST reject unpaired connections immediately after the initial `describe`/`info` exchange, and only paired satellites may proceed to operational events
- **FR-007**: System MUST advertise available capabilities (STT models, wake words, TTS voices) via Wyoming `describe`/`info` protocol events
- **FR-008**: System MUST support Zeroconf/mDNS service discovery for automatic detection by Home Assistant, advertised from the `lucia.AgentHost` process
- **FR-009**: System MUST include Voice Activity Detection (VAD) to segment speech from silence before STT processing
- **FR-010**: System MUST support configurable audio formats (sample rate, bit depth, channels) with automatic resampling when needed

#### Phase 2 — Diarization & Command Routing

- **FR-011**: System MUST implement a speaker-verification engine (the only supported "diarization" behavior in this feature) that extracts speaker embeddings from single utterances using sherpa-onnx speaker models
- **FR-012**: System MUST implement a command pattern matcher that routes recognized patterns directly to existing Lucia skills (LightControlSkill, ClimateControlSkill, SceneControlSkill, etc.) without LLM involvement
- **FR-013**: System MUST implement confidence-based routing with configurable thresholds: above threshold → fast-path execution, below threshold → LLM orchestrator handoff
- **FR-014**: System MUST enrich LLM-routed requests with speaker verification context (speaker ID, confidence, partial matches) to improve orchestrator accuracy
- **FR-015**: System MUST support speaker enrollment for per-user command authorization (optional, configurable)
- **FR-015a**: System MUST automatically create provisional speaker profiles when unrecognized voice embeddings are detected, storing embeddings and interaction count without requiring manual enrollment
- **FR-015b**: System MUST provide a guided voice onboarding flow that collects 5-10 diverse speech samples, extracts and averages speaker embeddings, and persists the enrolled profile — initiatable via dashboard or voice command
- **FR-015c**: System MUST support an "ignore unknown voices" configuration mode that silently discards commands from speakers not matching any enrolled profile above the configured similarity threshold
- **FR-015d**: System MUST surface enrollment suggestions (via dashboard notification and optional TTS prompt) when a provisional speaker profile accumulates a configurable number of interactions (default: 5)
- **FR-015e**: System MUST support adaptive voice profiles that optionally update enrolled speaker embeddings incrementally from high-confidence matches, compensating for natural voice changes over time
- **FR-015f**: System MUST support user-defined custom wake words using sherpa-onnx open-vocabulary keyword spotting, requiring only text input (no audio training) to activate a new wake phrase
- **FR-015g**: System MUST provide a browser-based onboarding page, available only after Lucia setup is complete, that guides users through voice profile enrollment AND optional wake word calibration in a single session
- **FR-015h**: System MUST support optional wake word calibration where 3-5 spoken samples are recorded via the browser, analyzed for detection confidence, and used to auto-tune per-user boost scores and detection thresholds
- **FR-015i**: System MUST support multiple concurrent wake words (one per enrolled user plus a system default) with independent detection thresholds
- **FR-015j**: The browser onboarding page MUST use the Web Audio API (`getUserMedia`) for microphone access, provide real-time audio level visualization, and be responsive for mobile, tablet, and desktop browsers
- **FR-016**: System MUST integrate with the existing `LuciaEngine` contract in Phase 1 for LLM fallback, preserving session/context continuity, and MUST extend that contract in Phase 2 for metadata-rich voice-native invocation rather than assuming the current text API is sufficient

#### Phase 3 — TTS Synthesis

- **FR-017**: System MUST integrate Qwen3-TTS via direct ONNX Runtime inference, including a C# port of the tokenizer/front-end, for high-quality local text-to-speech synthesis
- **FR-018**: System MUST integrate Chatterbox Turbo TTS via direct ONNX Runtime inference as an alternative/selectable TTS engine
- **FR-019**: System MUST handle Wyoming `synthesize` events and return streaming `audio-start`/`audio-chunk`/`audio-stop` response sequences
- **FR-020**: System MUST support voice selection from available preset voices and configurable default voice per satellite
- **FR-021**: System MUST support optional voice cloning from reference audio samples (Qwen3-TTS and Chatterbox both support this)
- **FR-022**: System MUST implement TTS output caching for frequently spoken phrases (e.g., "lights turned on", "okay") to reduce synthesis latency

#### Phase 4 — Integration & Polish

- **FR-023**: System MUST implement the full in-process voice pipeline: wake word → STT → speaker verification/route → execute → TTS → playback
- **FR-024**: System MUST support `continue_conversation` semantics, returning to STT listening after TTS playback when the agent signals more input is needed
- **FR-025**: System MUST support multiple concurrent satellite sessions mapped to independent Lucia sessions via contextId within the AgentHost-hosted Wyoming listener
- **FR-026**: System MUST implement graceful degradation when optional components are unavailable (e.g., TTS disabled → text-only responses; speaker verification disabled → all commands routed to LLM)
- **FR-027**: System MUST integrate with the Aspire dashboard for health monitoring, resource lifecycle management, and log viewing
- **FR-028**: System MUST emit OpenTelemetry spans and metrics for each pipeline stage with standardized naming conventions

### Non-Functional Requirements

- **NFR-001**: STT transcription latency MUST be < 500ms from `audio-stop` to `transcript` for utterances under 10 seconds on a modern x86_64 CPU (4+ cores)
- **NFR-002**: Wake word detection latency MUST be < 300ms from wake phrase completion to `detection` event
- **NFR-003**: Fast-path command execution (speaker verification → skill) MUST complete in < 200ms from transcript availability
- **NFR-004**: TTS time-to-first-audio MUST be < 500ms for phrases under 50 words using Chatterbox Turbo
- **NFR-005**: End-to-end pipeline latency (end-of-speech to start-of-TTS) MUST be < 1.5s for fast-path commands and < 5s for LLM-routed commands
- **NFR-006**: System MUST support at least 20 concurrent always-on wake word streams from Wyoming satellites, with a default `MaxWakeWordStreams` of 30. STT, speaker verification, and TTS operate as short burst sessions (typically 1-2 concurrent) triggered only after wake word detection.
- **NFR-007**: Memory usage for Wyoming voice services within AgentHost MUST not exceed: +200MB baseline (models + framework), +5MB per always-on wake word stream, +50MB per active STT session (burst), +6GB for TTS models (GPU). On reference hardware (RTX 3080 12GB, 32GB RAM, 8-core CPU), the system should comfortably support 20-30 always-on satellites.
- **NFR-008**: All ONNX model inference MUST run locally without network access (privacy-first)
- **NFR-009**: System MUST support both CPU and GPU (CUDA/DirectML) inference with runtime selection
- **NFR-010**: System MUST distinguish between always-on wake word streams (lightweight, continuous, one per satellite) and burst processing sessions (STT, speaker verification, TTS — heavyweight, short-lived, triggered by wake detection). Wake word streams MUST NOT be limited by the STT/TTS concurrency budget.

### Key Entities

- **WyomingSession**: Represents an active TCP connection from a Wyoming satellite handled by the in-process Wyoming listener, including connection metadata, audio format negotiation state, and associated Lucia session/context IDs
- **AudioPipeline**: Represents the processing chain for a single utterance: raw PCM → VAD → STT → transcript, with intermediate buffering and streaming state
- **WakeWordDetector**: Represents an active keyword spotting session, continuously processing audio chunks and emitting detection events
- **SpeakerProfile**: Represents an enrolled speaker with stored embeddings for speaker-verification-based identification and optional authorization rules
- **CommandPattern**: Represents a registered fast-path command template with skill binding, entity extraction rules, and confidence thresholds
- **TTSEngine**: Represents a loaded TTS model (Qwen3-TTS or Chatterbox) with synthesis configuration, voice selection, and caching state
- **VoicePipelineSession**: Represents the full end-to-end pipeline state for a single interaction, tracking progression through wake → STT → route → execute → TTS stages

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Wyoming server successfully discovered and connected by Home Assistant Wyoming integration within 30 seconds of startup
- **SC-002**: Streaming STT achieves ≥ 90% word accuracy on the LibriSpeech test-clean benchmark using the default sherpa-onnx model
- **SC-003**: Wake word detection achieves ≥ 95% recall with < 1 false positive per hour in a quiet home environment
- **SC-004**: Diarization fast-path routes ≥ 70% of common home automation commands without LLM involvement
- **SC-004a**: With "ignore unknown voices" enabled, system achieves ≥ 98% rejection rate for non-enrolled speaker audio (TV, music, other household members) while maintaining ≥ 95% acceptance rate for enrolled speakers
- **SC-004b**: New user completes full onboarding (voice profile + custom wake word + calibration) in under 3 minutes via browser on a phone
- **SC-005**: Fast-path command latency is < 500ms end-to-end (audio-stop → command executed) for 95th percentile
- **SC-006**: TTS synthesis produces intelligible, natural-sounding speech rated ≥ 3.5/5 MOS (Mean Opinion Score) in informal user testing
- **SC-007**: Full pipeline (wake → STT → fast-path → TTS) completes in < 2 seconds for simple commands on reference hardware (Intel i5-12400 or equivalent)
- **SC-008**: System passes all Wyoming protocol compliance tests from the OHF-Voice test suite
- **SC-009**: OpenTelemetry traces capture timing for every pipeline stage with < 1ms instrumentation overhead
- **SC-010**: System runs stably for 24+ hours of continuous operation with periodic voice interactions without memory leaks or degraded performance
