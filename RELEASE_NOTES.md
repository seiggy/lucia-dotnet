# Release Notes - 1.2.2

**Release Date:** April 2026  
**Code Name:** "Guardrail"

---

## 🛡️ Overview

"Guardrail" is a targeted bugfix release addressing three user-reported issues: dashboard navigation, UI scroll jitter, and the fast-path pattern matcher incorrectly claiming non-light device commands. The pattern matcher now correctly bails to the LLM for fans, ACs, TVs, locks, and other non-light devices, and the temporal bail-signal logic was refined to stop false-positiving on spatial prepositions like "in the kitchen."

## 🐛 Bug Fixes

### Dashboard Navigation (#113)
- **"Back to Traces" link** now navigates to `/traces` instead of `/` (which is the Activity page). Reported by @paulglavin.

### Entity Checkbox Scroll (#113)
- **BulkActionBar layout shift** — clicking entity checkboxes no longer causes the page to scroll. The bulk action bar now collapses via CSS (`h-0 invisible overflow-hidden`) instead of unmounting from the DOM, eliminating the layout shift that occurred when transitioning between 0 and 1+ selected items.

### Fast-Path Pattern Matcher — Non-Light Device Bail
- Commands like "turn office fan on", "turn off the AC", "turn on the TV" no longer match `LightControlSkill`. A new **non-light device token set** (fan, ac, tv, lock, door, speaker, vacuum, etc.) causes the pattern matcher to reject the match and defer to the LLM orchestrator.
- **Temporal preposition fix** — "in" and "at" no longer unconditionally trigger bail signals. They now only bail when followed by a number, duration word, or digit-prefixed time token (e.g., "7pm", "10am", "30min"), not when used as spatial prepositions ("lights on in the kitchen"). This fixes a pre-existing false positive in area-based light commands.

## 🧪 Tests

- 20 new `CommandPatternMatcherTests`: temporal bail (4), spatial pass-through (2), fan bail (4), other non-light devices (6), light-word exception regressions (4)
- All 35 pattern matcher tests passing
- 278/279 Wyoming test suite passing (1 pre-existing DI registration test unrelated to this release)

## 📁 Files Changed

| File | Change |
|------|--------|
| `lucia-dashboard/src/pages/TraceDetailPage.tsx` | Fix `navigate('/')` → `navigate('/traces')` |
| `lucia-dashboard/src/pages/EntityLocationPage.tsx` | BulkActionBar CSS collapse instead of unmount |
| `lucia.Wyoming/CommandRouting/CommandPatternMatcher.cs` | Non-light device bail + temporal preposition bigram check |
| `lucia.Tests/Wyoming/CommandPatternMatcherTests.cs` | 10 new test cases |

---

# Release Notes - 1.2.1

**Release Date:** April 2026  
**Code Name:** "Searchlight"

---

## 🔦 Overview

"Searchlight" is a quality-focused release that dramatically improves the orchestrator's intent routing accuracy — particularly for smaller local models like Gemma 4. Starting from a Gemma 4 routing trace where "turn off the lights in Zack's Office" was confidently misrouted to the climate agent, this release introduces a comprehensive evaluation suite, systematic prompt engineering for small-model compatibility, and timer-agent routing support. Orchestrator eval coverage went from 7 scenarios to 56, and Gemma 4 routing accuracy went from ~0% (infrastructure crashes) to 100% on the final test run.

## 🚀 Highlights

- **Comprehensive Orchestrator Eval Suite** — 56 YAML scenarios and 24 xUnit tests covering all 7 agent types (light, climate, scene, lists, music, general, timer) with cross-domain confusion tests, multi-agent splitting, ambiguous requests, STT variants, and entity ID patterns.
- **Router Prompt Engineering for Small Models** — Rule 0 "Time-Delayed Action Priority," domain inference hints, multi-domain detection rules, and skill example injection that enable even 9B parameter models to correctly route compound requests.
- **Timer Agent Routing** — Full timer/scheduler/alarm routing support with cross-domain guards ensuring "turn off the AC in 5 minutes" goes to timer-agent, not climate-agent.
- **EvalTestFixture Overhaul** — All 7 agent cards now registered in the mock registry (was only 3), plus AgentSessionStore cast fix that was crashing all pipeline tests.
- **Multi-Backend Eval Comparison** — New benchmark tooling for comparing routing accuracy across different Ollama models.
- **Climate Agent Gemma 4 Compatibility** — Direct-action rules and two-step Find→Set tool pattern expectations for smaller models.

## ✨ Features

### Orchestrator Evaluation Suite
- 56 YAML eval scenarios in `orchestrator.yaml` (up from 7)
- 24 xUnit tests in `OrchestratorEvalTests.cs` (up from 7)
- Categories: basic routing, room-specific, cross-domain confusion, multi-agent, ambiguous, STT variants, timer/scheduler, entity IDs
- Negative assertions: light requests assert NOT climate, timer requests assert NOT device agents
- Timer-agent card registered in `EvalTestFixture` with full dependency wiring

### Router Prompt Improvements
- **Rule 0 — Time-Delayed Action Priority**: Checked BEFORE agent domain matching; "in X minutes" / "at X PM" → timer-agent regardless of device mentioned
- **Rule 8 — Domain Inference Hints**: Implicit language mapping (warmer→climate, bright→light, play→music, bedtime→scene, timer→timer-agent)
- **Rule 9 — Multi-Domain Detection**: Explicit split rules with examples; prohibits collapsing multi-intent requests into general-assistant
- **IncludeSkillExamples enabled**: Agent catalog now includes per-agent example prompts for pattern matching

### Multi-Backend Benchmark Comparison
- Side-by-side eval runs across different Ollama models
- Backend selector UI with Spectre.Console rendering
- Auto-tracing for scenario evaluation

## 🐛 Bug Fixes

- **EvalTestFixture only registered 3 of 7 agent cards** — Climate, lists, scene, and timer cards were extracted but never passed to mock registry, making it impossible to catch cross-domain routing bugs
- **AgentSessionStore cast error** — FakeItEasy's `IServiceProvider` returned a Castle proxy that couldn't be cast to concrete `AgentSessionStore`; configured to return null so `NoopAgentSessionStore` fallback kicks in
- **A2AToolCallEvaluator crash on ambiguous tests** — Removed evaluator from tests that intentionally have no expected agent IDs
- **Spectre markup escape in backend selector** — Brackets in display strings caused rendering errors
- **Climate agent two-step tool pattern** — Updated eval scenarios to expect Find→Set pattern for Gemma 4

## 📊 Test Results

### Gemma 4 (kavai/Gemma4-GPT5:e2b) Orchestrator Routing
| Metric | Before | After |
|--------|--------|-------|
| Test infrastructure | ❌ All crashed | ✅ All pass |
| Routing accuracy | 0/20 (0%) | 24/24 (100%) |
| Agent coverage | 3 of 7 agents testable | 7 of 7 agents testable |
| YAML scenarios | 7 | 56 |
| xUnit test methods | 7 | 24 |

## 📋 Breaking Changes

None. All changes are additive — new tests, expanded YAML scenarios, and prompt improvements that are backward-compatible with existing agent configurations.

## 🔮 Known Issues

- **Timer skill cross-visibility**: `ListTimers` doesn't show scheduled actions created via `ScheduleAction` — they're in separate stores. Ask "any scheduled tasks?" instead of "any timers?" as a workaround.
- **Dashboard Tasks page**: Shows A2A platform tasks (agent execution traces) instead of user-created scheduled tasks from `ScheduledTaskStore`.

---
---

# Release Notes - 1.2.0

**Release Date:** March 2026  
**Code Name:** "Pulsar"

---

## 🎙️ Overview

"Pulsar" introduces the **Wyoming Voice Platform** — a full local speech pipeline that turns Lucia into a Wyoming protocol-compatible voice satellite for Home Assistant. This release delivers streaming speech-to-text, speaker verification, wake word detection, speech enhancement, a multi-engine model management system, and a new dashboard Voice Platform page for configuring everything from one surface. In addition, we introduce the **Conversation Command Parser** — a fast-path processing pipeline that pattern-matches common voice commands (lights, climate, scenes) and executes them directly against Home Assistant, bypassing the LLM entirely for sub-50ms response times. We also introduce user-facing personalization to Lucia's orchestration layer, letting users define a personality prompt that rewrites all agent responses through an LLM before delivery, and introduces **Pluggable Data Providers** — an agnostic persistence layer that enables mono-container deployment as a Home Assistant add-on with InMemory caching and SQLite storage, eliminating the need for Redis and MongoDB on resource-constrained devices.

## 🚀 Highlights

- **Wyoming Protocol Server** — Native TCP server implementing the Home Assistant Wyoming satellite protocol for real-time voice processing.
- **Multi-Engine STT Pipeline** — Hybrid streaming STT with progressive re-transcription, Sherpa streaming, Sherpa offline (Parakeet TDT/CTC), and IBM Granite 4.0 1B Speech ONNX engines.
- **Speaker Verification & Profiling** — Cosine-similarity speaker identification with enrolled profiles, provisional auto-discovery, adaptive profile updates, and guided voice enrollment onboarding.
- **ONNX Auto-Detection** — Automatic GPU/accelerator selection (CUDA, ROCm, OpenVINO, DirectML, CoreML) across all six inference engines — no manual configuration required.
- **GTCRN Speech Enhancement** — Real-time streaming noise reduction for cleaner audio in noisy environments.
- **Voice Platform Dashboard** — New unified control room for model management, speaker profiles, wake words, engine status, and real-time session monitoring.
- **Personality Prompt** — Configurable system prompt that rewrites the fan-in aggregated response through an LLM, giving Lucia a customizable personality (pirate speak, formal assistant, casual friend — you name it).
- **Conversation Command Parser** — New `POST /api/conversation` endpoint with pattern-matching pipeline that executes common smart home commands (lights, climate, scenes) directly via skills — zero LLM latency for recognized commands, SSE-streamed LLM fallback for everything else.
- **Response Templates** — Customizable response templates stored in MongoDB with `{placeholder}` interpolation. Manage templates per skill/action from the dashboard with guided dropdowns and token insertion buttons.
- **Home Assistant Component v1.2** — Simplified integration migrated from A2A JSON-RPC to structured REST. No more agent catalog selection — just point to the host and go.
- **Separate Model Support** — Personality rewriting can use a different (cheaper/faster) model than the orchestrator, selectable from a dropdown of configured chat-type providers.
- **Zero-Cost Opt-In** — When no personality is configured, the pipeline is unchanged — no LLM call, no added latency.
- **Pluggable Data Providers** — Agnostic persistence layer with InMemory (replaces Redis) and SQLite (replaces MongoDB) alternatives, configurable via `DataProvider:Cache` and `DataProvider:Store`.
- **Home Assistant Mono-Container** — New `Dockerfile.ha` for single-container deployment with zero external dependencies, CPU-only ONNX, and embedded dashboard.
- **CpuOnly Build Flag** — MSBuild property `/p:CpuOnly=true` excludes GPU ONNX packages for clean CPU-only container builds.

## ✨ Features

### Wyoming Protocol Server
- Full Wyoming satellite protocol implementation (audio-start, audio-chunk, audio-stop, transcribe, transcript, detect, not-detected)
- Persistent TCP connections with per-session state management
- Zeroconf/mDNS service advertisement for automatic Home Assistant discovery
- VAD-driven voice activity detection with configurable speech/silence thresholds
- Wake word detection with custom phrase support and per-speaker calibration

### Multi-Engine Speech-to-Text
- **HybridSttEngine** — Streams audio through a lightweight online model, then re-transcribes the full utterance with a high-accuracy offline model for best-of-both-worlds latency and accuracy
- **SherpaSttEngine** — Pure streaming CTC/transducer inference for ultra-low-latency transcription
- **SherpaOfflineSttEngine** — Offline NeMo Parakeet TDT+CTC support for high-quality batch transcription
- **GraniteOnnxEngine** — IBM Granite 4.0 1B Speech ONNX with 3-model pipeline (audio encoder, embed tokens, auto-regressive decoder with 40-layer KV cache)
- Progressive re-transcription with burst detection and stability-based early stopping
- Per-engine model catalog with download, install, activate, and delete lifecycle

### Speaker Verification & Voice Profiles
- Sherpa-onnx speaker embedding extraction with cosine similarity matching
- Enrolled speaker profiles with configurable verification threshold
- Provisional auto-discovery profiles for unknown speakers with interaction tracking
- Adaptive profile updates (exponential moving average) for high-confidence matches
- Guided onboarding flow with audio quality validation (SNR, duration checks)
- Profile merge, rename, and provisional-to-enrolled promotion via API and dashboard
- Audio clip capture, playback, reassignment, and per-profile management
- Redis-cached enrolled profiles for sub-millisecond lookup latency
- MongoDB-backed persistent storage with in-memory fallback

### ONNX Provider Auto-Detection
- `OnnxProviderDetector` singleton probes `OrtEnv.Instance().GetAvailableProviders()` at startup
- Automatic selection priority: CUDA → ROCm → OpenVINO → DirectML → CoreML → CPU
- Applied to all six engines: SherpaDiarization, HybridSTT, SherpaStt, SherpaOfflineSTT, GraniteOnnx, GtcrnSpeechEnhancer
- Detected provider exposed in `/api/wyoming/status` and displayed on the dashboard status card with GPU badge
- Graceful fallback — if an accelerated provider fails to initialize, falls through to CPU

### Speech Enhancement
- GTCRN (Gated Temporal Convolutional Recurrent Network) streaming noise reduction
- Per-session enhancement with isolated overlap-add state
- Raw audio preserved separately for speaker verification to avoid spectral mismatch with enrollment data

### Model Management System
- Centralized model catalog with architecture-aware compatibility filtering
- Background download with real-time progress tracking via SSE
- Multi-stage progress bars (download → extract → validate)
- Hot-reload model activation via `ActiveModelChanged` events — no restart required
- Per-engine model views (STT, VAD, Wake Word, Speaker Embedding, Speech Enhancement)
- Startup model validation with dummy inference warmup

### Voice Platform Dashboard
- **Status tab** — Engine readiness tiles, active model indicators, ONNX provider detection display, and guided next-steps checklist
- **Models tab** — Browse, download, activate, and delete models per engine type with real-time download progress
- **Profiles tab** — View enrolled and provisional speaker profiles, inline rename, promote to enrolled, merge profiles, manage audio clips with playback
- **Wake Words tab** — Register custom wake phrases with optional speaker enrollment, manage and delete entries
- **Monitor tab** — Real-time session monitoring via SSE with audio level meters, transcript log, and connection status
- **Config panel** — Voice verification threshold, provisional profile settings, adaptive profiles, and retention controls — persisted to MongoDB via ConfigStoreWriter

### 🎭 Personality Prompt (PR #80)
Users can now define a personality prompt on the `/configuration` page under the new Personality Prompt section. When set, the `ResultAggregatorExecutor` passes the composed multi-agent response through an LLM with the personality instructions as the system prompt, rewriting the output to match the desired tone.

- `PersonalityPromptOptions` — New config model with `Instructions` (the personality system prompt) and `ModelConnectionName` (optional separate LLM for rewriting)
- `WorkflowFactory` — Accepts `IOptionsMonitor<PersonalityPromptOptions>` for live config reload without restart. Conditionally resolves a separate `IChatClient` when `ModelConnectionName` is set, or falls back to the orchestrator's default model.
- `ResultAggregatorExecutor` — After composing the raw message from agent responses, calls the personality LLM with `system=instructions` + `user=composed message`. Graceful fallback to raw message on LLM failure or empty response.
- **Resilient resolution** — A misconfigured `ModelConnectionName` logs a warning and disables personality for that request instead of failing the entire orchestration pipeline.
- **Cancellation-safe** — `OperationCanceledException` propagates correctly through the personality rewrite path.

### 🖥️ Dashboard
- **Model provider dropdown** — The `ModelConnectionName` field renders as a searchable dropdown populated from configured chat-type model providers, matching the pattern used in Agent Definitions.
- **Textarea field type** — New `textarea` field type in the configuration page for multi-line prompt editing with vertical resize support.
- **Auto-discovery** — The Personality Prompt section appears automatically in the configuration sidebar via schema API.
- **Conversation Test Page** — Interactive chat interface at `/conversation` for testing the command parser endpoint directly from the dashboard, with HA context simulation, SSE streaming, and response metadata badges showing whether commands were parsed locally or handled by the LLM.
- **Response Templates Page** — Full CRUD management of response templates at `/response-templates`, grouped by skill. Features pattern-driven SkillId/Action dropdowns populated from the parser, token insertion buttons for `{entity}`, `{action}`, `{area}` placeholders, and template preview with sample values.
- **Activity Dashboard Metrics** — Three new summary cards: Command Parsed count, LLM Fallback count, and Parser Rate percentage showing the ratio of commands handled locally vs forwarded to the LLM.

### 💬 Conversation Command Parser
- **`POST /api/conversation`** — New REST endpoint accepting structured JSON with user text and separated device/session context object. Returns instant JSON for parsed commands or SSE streaming for LLM fallback.
- **True LLM Bypass** — Pattern-matched commands call skill methods (LightControlSkill, ClimateControlSkill, SceneControlSkill) directly against Home Assistant — zero LLM involvement, sub-50ms execution.
- **Command Pattern Matching** — Leverages the existing `CommandPatternRouter` with template syntax (`{name}`, `{name:opt1|opt2}`, `[optional]`), confidence scoring, and priority-based tiebreaking.
- **DirectSkillExecutor** — Dispatches matched routes to concrete skill methods: light toggle/brightness, climate set temperature/adjust, scene activate. Resolves entities via captured text and device area context.
- **Response Template System** — MongoDB-stored templates with simple `{placeholder}` interpolation, random variant selection for natural variety, and seed defaults for all supported commands. Managed via CRUD API and dashboard.
- **Context Reconstructor** — Rebuilds system prompt from structured context JSON for LLM fallback, matching the format the orchestrator expects.
- **Conversation Telemetry** — OpenTelemetry counters (`conversation.command_parsed`, `conversation.llm_fallback`, `conversation.command_parsed.errors`) and duration histograms, plus in-memory stats for the activity dashboard.
- **Multi-Turn Continuity** — Server generates stable `conversationId` (GUID) when client doesn't provide one, used consistently across command and LLM paths.
- **`GET /api/conversation/patterns`** — Exposes registered command patterns with their SkillId, Action, placeholder tokens, and example templates for dashboard tooling.

### 🏠 Home Assistant Component (v1.2.0-preview.1)
- **REST Migration** — Component migrated from A2A JSON-RPC 2.0 to the new `POST /api/conversation` endpoint with structured context objects.
- **Simplified Setup** — Removed agent catalog fetch and agent selection. Configuration requires only host URL + API key.
- **SSE Streaming** — Handles both instant JSON (command parsed) and SSE event streams (LLM fallback) from the conversation endpoint.
- **Structured Context** — Device ID, area, type, user ID, timestamp, and location sent as typed JSON fields instead of embedded in prompt text.
- **Auto-Generated Conversation IDs** — Generates UUID for first-turn conversations, maintaining multi-turn continuity via the tracker.
- **Optional Prompt Override** — Configurable prompt template override in the HA options flow.

### 🧺 Project Laundry
- **SDK Pinned** — Pinned .NET 10 to the latest appropriate minimal SDK
- **Removed outdated docs** — Removed a lot of outdated docs and old AI assistance templates and prompts.
- **Package Updates** — Updated all central projects to the latest versions from .NET 10.0.4 security patch

### 🔌 Pluggable Data Providers

The new `lucia.Data` project introduces provider-agnostic persistence, enabling Lucia to run without Redis or MongoDB:

**Cache Providers (replacing Redis):**
- `InMemorySessionCacheService` — Multi-turn conversation state with sliding expiration
- `InMemoryDeviceCacheService` — Home Assistant entity caching with TTL support
- `InMemoryPromptCacheService` — Routing and response caching with SHA256 + semantic similarity matching
- `InMemoryTaskStore` — A2A task persistence with configurable TTL and periodic cleanup
- `InMemoryEntityLocationService` — Floor/area/entity graph with hybrid entity matching

**Store Providers (replacing MongoDB):**
- 17 SQLite repository implementations covering all 4 MongoDB databases (luciaconfig, luciatraces, luciatasks, luciawyoming)
- Hybrid schema: indexed columns for query performance + JSON blob for full document storage
- Code-first migrations with `SqliteMigrationRunner` and versioned schema tracking
- `SqliteConfigurationProvider` with polling-based hot-reload matching the MongoDB provider pattern

**Configuration:**
```json
{
  "DataProvider": {
    "Cache": "InMemory",
    "Store": "SQLite",
    "SqlitePath": "./data/lucia.db"
  }
}
```

**Interface Abstractions:**
- `IConfigStoreWriter` — New interface for configuration CRUD (extracted from `ConfigStoreWriter`)
- `ITaskIdIndex` — New interface for task ID enumeration (extracted from Redis-specific key scanning)
- `ConfigSeeder` refactored to use `IConfigStoreWriter` instead of `IMongoClient`
- `TaskArchivalService` refactored to use `ITaskIdIndex` instead of `IConnectionMultiplexer`
- All API endpoints updated to use abstractions (no direct `IMongoClient` or `IConnectionMultiplexer` injection)

### 📦 Home Assistant Mono-Container

New `infra/docker/Dockerfile.ha` for resource-constrained deployment:

- **Zero Dependencies** — No Redis, MongoDB, or GPU drivers required
- **CPU-Only ONNX** — `CpuOnly=true` MSBuild flag excludes `Microsoft.ML.OnnxRuntime.Gpu.Linux`, verified ONNX provider detection with graceful CPU fallback
- **Embedded Dashboard** — React SPA built and bundled into `wwwroot/` in a multi-stage Docker build
- **SQLite Persistence** — Single `/data/lucia.db` file for all configuration, traces, tasks, and voice data
- **Volume Mounts** — `/data` (SQLite DB), `/app/models` (Wyoming voice models), `/app/plugins` (script plugins)
- **HA Add-on Support** — `ha-addon/config.yaml` with ingress, port mapping, and options schema

### 🔍 ONNX Provider Verification

`OnnxProviderDetector` now verifies each accelerated provider by attempting `SessionOptions.AppendExecutionProvider_*()` before selecting it. This prevents sherpa-onnx native crashes when CUDA shared libraries are absent — the detector gracefully falls back to `CPUExecutionProvider`.


## 🐛 Bug Fixes

- **CUDA GPU acceleration not activating** — `Microsoft.ML.OnnxRuntime` NuGet package was CPU-only. Replaced with `Microsoft.ML.OnnxRuntime.Gpu.Linux` 1.23.2 and aligned managed/native versions (was mismatched 1.23.2/1.22.0), enabling automatic `CUDAExecutionProvider` detection on hosts with NVIDIA GPUs. Docker voice image verified working with RTX 4090 + CUDA 12.8 + cuDNN 9.20.
- **Wyoming describe response missing STT** — `WyomingServiceInfo` injected a single `ISttEngine?` via DI, which resolved to the last registered engine (SherpaSttEngine). If that engine wasn't ready, STT was omitted from the `info` response even when HybridSttEngine was ready. Now injects `IEnumerable<ISttEngine>` and reports STT available if any engine is ready.
- **Parakeet model download "not found"** — `ModelCatalogService.GetModelById(string)` only searched `EngineType.Stt`, but Parakeet TDT is registered as `EngineType.OfflineStt`. Now searches all engine types. Also fixed the download endpoint to resolve the model base path per engine type instead of hardcoding the STT path.
- **Activating Parakeet crashes server** — `ModelManager.SwitchActiveModelAsync(string)` hardcoded `EngineType.Stt`, causing the streaming `OnlineRecognizer` to load an offline transducer model (native crash: `'window_size' does not exist in the metadata`). Now resolves engine type from the catalog, routing offline models to HybridSttEngine correctly.
- **Cannot switch back to streaming STT after activating offline model** — Both engines remained ready with no user preference tracking, so `FirstOrDefault(e => e.IsReady)` always picked HybridSttEngine (registered first). Added `ModelManager.PreferredSttEngineType` that tracks the user's last activation choice. Status endpoint, session engine selection, and active model API all respect the preference.
- **HuggingFace API key not persisting in dashboard** — `ConfigurationPage.entriesToValues` stripped only the first colon segment from stored keys (e.g., `Wyoming:HuggingFace:ApiToken` → `HuggingFace:ApiToken`). For nested config sections, this didn't match the schema property name `ApiToken`. Fixed to strip the full section prefix.
- **Speaker identification always returning unknown** — Speech enhancement was altering audio used for embedding extraction, causing ~0.39 cosine similarity against enrollment profiles. Now uses raw (unenhanced) audio for speaker verification.
- **Verification threshold changes from GUI ignored** — `SpeakerVerificationThreshold` config was never passed to `IdentifySpeaker()`. Now read via `IOptionsMonitor.CurrentValue` at identification time.
- **Embedding dimension mismatches silently failed** — After model changes, `CosineSimilarity` threw for mismatched dimensions, caught by outer try/catch returning null. Now gracefully skips with a warning log.
- **Voice config written to local JSON file** — `VoiceConfigApi` was writing to `voiceconfig.json` instead of the platform's MongoDB `ConfigStoreWriter`. Migrated to match established pattern.
- **Thread pool deadlock in STT finalization** — `GetFinalResult` blocked the thread pool. Converted to async with proper continuation.
- **Aspire resilience timeout killing model downloads** — 3-minute default timeout was insufficient for large model downloads. Bypassed Aspire service discovery for external URLs.
- **Progressive re-transcription mixing raw and enhanced audio** — STT now always receives raw audio; enhanced audio used only for clip storage.
- **WAV protocol wire format corrections** — Proper transcribe/transcript event ordering per Wyoming protocol spec.

## ⚡ Performance

- Hybrid STT achieves ~90ms finalization latency at 0% WER on benchmark audio
- Redis-cached speaker profiles reduce diarization lookup from ~500ms to ~1ms
- Short command fast-path skips progressive re-transcription overhead
- ONNX model warmup on startup eliminates cold-start inference latency
- CUDA 12.8 + cuDNN 9 GPU Dockerfile for accelerated voice inference
- Workstation GC mode and thread pool tuning for development responsiveness
- OTEL export frequency reduced from 1s to 5s to lower telemetry overhead

## 📊 Test Coverage

| Area | Tests |
|------|-------|
| Wyoming session integration | 50+ |
| Speaker verification components | 17 |
| Speech enhancement validation | 20+ |
| Engine hot-reload | 10+ |
| Engine readiness | 10+ |
| Wyoming status API | 2 |
| Command pattern matching | 30+ |
| Profile store (InMemory) | 10+ |
| Conversation command processor | 5 |
| Direct skill executor | 4 |
| Response template renderer | 4 |
| Context reconstructor | 4 |
| **Total Wyoming Tests** | **230** |
| **Total Conversation Tests** | **17** |

## 🔄 Upgrade Notes

- **New infrastructure dependencies** — MongoDB (`luciaconfig` database) is used for speaker profiles, voice config persistence, and response templates. Redis is optional but recommended for profile caching.
- **Home Assistant component upgrade** — The HA custom component has been migrated to v1.2.0-preview.1. **Breaking change:** remove and re-add the Lucia integration in Home Assistant. The new setup flow only requires the host URL and API key — agent selection is no longer needed.
- **Response templates** — Default templates are seeded automatically on first launch. Customize them via the Response Templates page in the dashboard or `POST /api/response-templates`.
- **Model downloads required** — On first launch, navigate to the Voice Platform → Models tab to download and activate at least one STT model and supporting models (VAD, Wake Word, Speaker Embedding).
- **Wyoming integration** — Add the Lucia Wyoming satellite in Home Assistant under Settings → Devices & Services → Add Integration → Wyoming. The server advertises via Zeroconf automatically.
- **GPU acceleration** — The project now ships with `Microsoft.ML.OnnxRuntime.Gpu.Linux` for automatic CUDA support. For local development, install CUDA Toolkit 12.x and cuDNN 9.x. The `OnnxProviderDetector` will find and use CUDA automatically — no configuration required. The Docker voice image (`Dockerfile.voice`) includes all GPU dependencies out of the box.
- **Existing voice config** — If you previously had a `voiceconfig.json`, those settings will need to be re-entered through the dashboard Voice Platform config panel (they now persist to MongoDB).
- **No breaking changes for data providers** — Default behavior (Redis + MongoDB) is unchanged. The new `DataProvider` configuration section is optional; omitting it preserves existing behavior. Set `DataProvider:Cache` to `InMemory` and `DataProvider:Store` to `SQLite` to switch to the embedded providers.

---
---

# Release Notes - 1.1.1

**Release Date:** March 7, 2026  
**Code Name:** "Spectra Patch 1"

---

## 🔧 Overview

v1.1.1 is a patch release addressing three bugs discovered shortly after the Spectra launch: an incorrect API integration in the Brave Search plugin, a data-loss issue when editing agent definitions, and a missing plugin update detection mechanism.

## 🐛 Bug Fixes

### Brave Search Plugin — LLM Context API ([#70](https://github.com/seiggy/lucia-dotnet/issues/70), [PR #72](https://github.com/seiggy/lucia-dotnet/pull/72))

The Brave Search plugin was using the raw Web Search API (`/v1/web/search`) which returns unprocessed search result links. It now uses the **LLM Context API** (`/v1/llm/context`), which returns pre-extracted, relevance-scored web content optimized for LLM consumption.

- Switched endpoint to `/v1/llm/context` with `maximum_number_of_tokens=8192`
- Updated response DTOs to match the grounding/sources schema
- Added `.Take(8)` limit on grounding items for consistent output size
- Added null guard on snippets to prevent `NullReferenceException`
- Bumped plugin version to 1.1.0
- Updated and expanded unit tests (6 passing)

### Agent Domain Settings Not Persisting ([#71](https://github.com/seiggy/lucia-dotnet/issues/71), [PR #73](https://github.com/seiggy/lucia-dotnet/pull/73))

Editing an agent's domain list in the Definitions page and clicking "Update" silently discarded the changes. Two root causes were identified and fixed:

- **Frontend**: `SkillConfigEditor` maintained its own React state for domain edits, but the parent form never flushed that state before saving. Added `forwardRef`/`useImperativeHandle` exposing a `saveAll()` method with dirty-section tracking to only persist modified sections.
- **Backend**: `UpdateDefinitionAsync` performed a full document replacement, clobbering system flags (`IsBuiltIn`, `IsRemote`, `IsOrchestrator`) and any fields omitted from the request. Switched to merge-based PUT that preserves existing values when incoming fields are null.
- Added 8 Playwright E2E tests covering domain persistence and system flag preservation.

### Plugin Update Detection ([#74](https://github.com/seiggy/lucia-dotnet/issues/74), [PR #75](https://github.com/seiggy/lucia-dotnet/pull/75))

The plugin system stored version information but never compared installed versions against repository manifests. Users had no way to know updates were available.

- Added `CheckForUpdatesAsync()` and `UpdatePluginAsync()` to `PluginManagementService` with semver-aware version comparison
- Added `GET /api/plugins/updates` endpoint to check for available updates
- Added `POST /api/plugins/{id}/update` endpoint to apply updates in-place
- Enhanced `GET /api/plugins/installed` response with `UpdateAvailable` and `AvailableVersion` fields
- Skip redundant downloads when installed version is already current
- Dashboard now shows "Update Available (v1.0.0 → v1.1.0)" badges with an Update button on the Plugins page
- 9 unit tests and 5 Playwright E2E tests covering the full update flow

## 📊 Test Coverage

| Area | Unit Tests | E2E Tests |
|------|-----------|-----------|
| Brave Search LLM Context API | 6 | — |
| Agent Domain Settings | — | 8 |
| Plugin Update Detection | 9 | 5 |
| **Total New Tests** | **15** | **13** |

## 🔄 Upgrade Notes

- **No breaking changes** — this is a fully backward-compatible patch release
- **Plugin update UI** — after upgrading, the Plugins page will automatically show update indicators for any outdated plugins
- **Restart required** — applying a plugin update from the dashboard will prompt for a restart to reload the updated plugin

---
---

# Release Notes - 1.1.0

**Release Date:** March 6, 2026  
**Code Name:** "Spectra"

---

## 🔮 Overview

"Spectra" delivers Lucia's largest entity-intelligence upgrade to date and folds in post-cut stabilization work across Home Assistant conversation flow, model-provider support, setup/plugins UX, and CI reliability. This release unifies entity modeling, introduces multi-signal matching, fixes semantic prompt-cache persistence and thresholding, and hardens the platform for day-to-day operation.

## 🚀 Highlights

- **Unified Entity Model** — All Home Assistant entity types now share a common `HomeAssistantEntity` base with domain-specific subtypes.
- **HybridEntityMatcher** — Multi-weighted search combines Levenshtein, Jaro-Winkler, phonetic matching, aliases, and token overlap.
- **Prompt Cache Overhaul** — Embeddings now persist correctly, routing/chat thresholds are split, and cache settings hot-reload without restart.
- **Embedding Resiliency + Live Progress** — Entity-location embeddings now generate in throttled background batches, expose SSE progress, and support per-item evict/regenerate controls.
- **Home Assistant Multi-turn Continuity** — Follow-up conversation flow restored and HA commit validation tightened.
- **OpenRouter + Discovery** — OpenRouter added as a first-class provider with model discovery support.
- **Plugin/Setup/Dashboard Stabilization** — Headless setup and HA setup-step UX improvements, plugin installation reliability fixes, and dashboard/tooling cleanup.
- **Brave Search API Plugin** — New privacy-respecting web search plugin using the Brave Search API, with full plugin config GUI for managing API keys from the dashboard.
- **CI Hardening** — HACS validation workflow corrected, deployment docs lint cleanup, and release pipeline reliability improvements.

## ✨ What's New

### 🔍 HybridEntityMatcher

- **Multi-signal scoring** — Combines normalized Levenshtein, Jaro-Winkler, token overlap, phonetic matching, and exact/prefix bonuses into a weighted composite score.
- **Configurable via `HybridMatchOptions`** — Tune weights, minimum thresholds, and phonetic algorithm (Soundex vs Double Metaphone) per search context.
- **`IMatchableEntity` interface** — Any entity type can participate in hybrid search by implementing this lightweight interface.
- **`EntityMatchResult`** — Rich result type carrying the matched entity, composite score, individual signal scores, and match metadata.

### 🏠 Unified Entity Architecture

- **`HomeAssistantEntity` base class** — Shared properties (entity ID, friendly name, area, floor, aliases, state, supported features as `SupportedFeatures` bitflag enum) for all HA entity types.
- **Domain subtypes** — `LightEntity`, `ClimateEntity`, `FanEntity`, `MusicPlayerEntity` extend the base with domain-specific attributes (color modes, HVAC modes, fan speeds, media metadata).
- **`EntityLocationService` refactor** — Centralized entity resolution service replaces per-skill `FindLightsByAreaAsync`/`FindLightAsync` methods. Skills now delegate entity lookup to the location service.
- **`AreaInfo` caching** — Areas now pre-build and cache entity collections, floor associations, and phonetic data for fast hierarchical search.
- **`FloorInfo` model** — First-class floor representation with area containment for multi-level home topologies.

### 👁️ Entity Visibility

- **`EntityVisibilityConfig`** — Per-entity visibility settings stored in MongoDB, letting users hide entities from Lucia without removing them from Home Assistant.
- **`EntityVisibilityApi`** — REST endpoints for bulk visibility management with area/domain filtering.
- **HA Exposed Entity List** — Support for pulling the pre-filtered exposed entity list from Home Assistant via WebSocket (`homeassistant/expose_entity/list`).

### 📡 Entity Embedding Resiliency + Live Progress

- **Name fallback for embedding safety** — When an entity/floor/area has no name or alias, matchable names now fall back to IDs (entity IDs strip domain and replace `_` with spaces) to avoid invalid empty embedding inputs.
- **Non-blocking embedding generation** — `EntityLocationService` now loads/cache location data first, then generates embeddings asynchronously in throttled batches with retry/backoff.
- **Batch embedding pipeline** — Uses provider batch generation (`GenerateAsync(IEnumerable<string>)`) to reduce request pressure and better handle provider-side limits.
- **Progress API + SSE stream** — Added `/api/entity-location/embedding-progress` and `/api/entity-location/embedding-progress/live` for real-time generation status.
- **Per-item cache controls** — Added embedding eviction/regeneration endpoints for floors, areas, and entities.
- **Dashboard live status** — Entity Location page now shows a live generation progress bar, missing-embedding count, and updates without manual refresh.

### 🧠 Prompt Cache Overhaul

- **Embedding persistence fixed** — Removed `[JsonIgnore]` from `CachedPromptEntry.Embedding` and `CachedChatResponseData.Embedding` that prevented embeddings from ever being serialized to Redis.
- **Split thresholds** — `SemanticSimilarityThreshold` (routing, default 0.95) and `ChatCacheSemanticThreshold` (chat, default 0.98) are independently configurable.
- **Hot-reload via `IOptionsMonitor`** — Cache thresholds update within seconds of dashboard config changes, no restart required.
- **Hit count tracking fix** — Routing cache exact-match hits now correctly persist the incremented `HitCount` back to Redis.
- **Normalized prompt keys** — `NormalizePrompt` applies `Trim().ToLowerInvariant()` for stable, case-insensitive cache keys.
- **User-text-only embedding** — Chat cache embeddings are computed from the user's request text only.

### 🏠 Home Assistant Conversation Flow

- Restored multi-turn continuity behavior for follow-up requests.
- Enforced stricter HA-side commit validation in automation and tool flows.
- Addressed follow-up review feedback for the multi-turn flow fix set.

### 🤖 Model Providers

- Added OpenRouter as a provider option in model-provider interactions.
- Expanded model discovery flow and post-review provider hardening.

### 🔌 Setup, Plugins, and Dashboard

- Improved headless setup and Home Assistant setup-step UX.
- Fixed plugin tooling/integration issues that blocked reliable plugin installation.
- Resolved setup/plugin/dashboard review and lint issues.

### 🌐 Brave Search API Plugin

- **New web search provider** — `plugins/brave-search/plugin.cs` implements `IWebSearchSkill` using the [Brave Search API](https://brave.com/search/api/) as an alternative to SearXNG.
- **API key authentication** — Configurable via `BraveSearch:ApiKey`, `BRAVE_SEARCH_API_KEY` env var, or the new plugin config GUI.
- **Full telemetry** — OpenTelemetry `ActivitySource` and `Meter` instrumentation for request counts, failure counts, and search duration.
- **Provider-agnostic agent** — General Agent skill description no longer references SearXNG; works with whichever search plugin is active.

### ⚙️ Plugin Configuration GUI

- **Schema-driven config** — Plugins can now declare configurable properties via `ConfigSection`, `ConfigDescription`, and `ConfigProperties` on the `ILuciaPlugin` interface (default interface members — non-breaking).
- **`PluginConfigProperty` record** — Defines name, type, description, default value, and sensitivity for each config field.
- **`GET /api/plugins/config/schemas`** — New endpoint aggregates config schemas from all loaded plugins.
- **Dashboard Configuration tab** — New tab on the Plugins page renders dynamic forms from plugin schemas, with sensitive-field masking, save/discard, and toast notifications.
- **SearXNG retrofitted** — SearXNG plugin now declares its `BaseUrl` in the config schema so it can also be configured from the UI.

### 🐛 Matcher Debug API

- **`/api/matcher/debug`** — Interactive endpoint for testing `HybridEntityMatcher` queries against live entity data with per-signal score breakdowns.
- **Dashboard page** — `MatcherDebugPage.tsx` provides a UI for experimenting with matcher queries and score distributions.

### 🔧 Skill Optimizer Migration

- **`GetCachedEntitiesAsync` removed** from `IOptimizableSkill` — Entity resolution now flows through `EntityLocationService`.
- **Legacy skill methods retired** — `LightControlSkill.FindLightsByAreaAsync`, `FindLightAsync`, `GetLightStateAsync`, and `SetLightStateAsync` now throw `NotSupportedException`.
- **Device cache dependencies removed** — `LightAgent` no longer depends directly on embedding provider or device cache.

### 🎯 Skill Optimizer Enhancements

- **Import from Traces** — Fixed and operational. Extracts search terms from traced tool calls for the skill's owning agent, using skill-declared `SearchToolNames` and `AgentId` instead of hardcoded values.
- **Multi-entity expected results** — Test cases now support multiple expected entities (e.g., "kitchen lights" → 2 light entities). New `EntityMultiSelect` component with chip display, searchable dropdown, and keyboard navigation (↑/↓/Enter/Tab/Escape/Backspace).
- **Partial scoring** — Recall (3.0 max) goes negative when expected entities are missed or when results exceed the max count. Precision (1.0 max) rewards exact count matches with partial credit for extras within limits.
- **Export test dataset** — New "Export Dataset" button downloads a JSON file containing test cases + entity location data for offline replay and issue reporting.

### 🕵️ Agent Impersonation & Entity Debugging

- **Impersonate Agent filter** — New dropdown on Entity Locations page (Entities + Search tabs) filters the entity view to what a specific agent can see. Auto-applies the agent's configured entity domains and visibility restrictions.
- **Agent domain metadata** — `available-agents` API returns `{name, domains}` objects by aggregating `EntityDomains` from `IOptimizableSkill` registrations. Domainless agents are filtered out.
- **Server-side domain+agent filtering** — Entity and search endpoints accept comma-separated `domain` and `agent` query parameters for combined filtering.
- **Entity removal** — Delete button on Entity Locations page now fully removes entities from the cache (not just embeddings), fixing issues with stale/duplicate entity IDs.

### ⚙️ Configurable Skill Domains & Agent Definitions

- **`ISkillConfigProvider` interface** — Agents expose their skill config sections with `OptionsType` for schema generation. Implemented by LightAgent, ClimateAgent, SceneAgent, and MusicAgent.
- **Dynamic skill config editor** — Agent Definitions page renders a schema-driven form for each agent's skill options (EntityDomains, matcher thresholds, etc.). Controls: tag picker for string arrays (backed by available HA domains), range sliders for numbers, integer inputs.
- **`EntityDomains` via `IOptionsMonitor`** — All skill options classes (`LightControlSkillOptions`, `ClimateControlSkillOptions`, `FanControlSkillOptions`, `SceneControlSkillOptions`, `MusicPlaybackSkillOptions`) now include `EntityDomains` with hot-reload from MongoDB config.
- **`IAgentSkill.EntityDomains`** — All agent skills declare their operating domains. `SearchHierarchyAsync` calls use the options-backed value instead of hardcoded arrays.
- **Config seeder upgrade** — Seeds missing config sections on restart (not just when collection is empty), handling upgrades gracefully.
- **Skill sections hidden from raw Config page** — Skill config is managed exclusively through the Agent Definitions page.

### 🎵 Music Agent Consolidation

- **In-process execution** — Music Agent moved from a separate A2A container to always running in-process with AgentHost. Enables `IOptimizableSkill` and `ISkillConfigProvider` visibility in the AgentHost DI container.
- **`IEntityLocationService` migration** — Replaced ~300 lines of custom entity resolution (Redis cache, embeddings, Music Assistant-specific filtering, cosine similarity) with the shared `SearchHierarchyAsync` pipeline.
- **`MusicPlaybackSkillOptions`** — New options class with `EntityDomains` defaulting to `["media_player"]` plus standard matcher params, hot-reloaded via `IOptionsMonitor`.
- **Kubernetes cleanup** — Removed music-agent Service, Deployment, and image overrides from helm chart and raw manifests. Only timer-agent remains as an external A2A pod.

### 🐳 CI/CD and Delivery

- Added support for pre-release Docker tags without moving `latest`.
- Fixed CI workflow ordering to check out repository contents before HACS validation.
- Fixed deployment markdownlint issues and removed defunct documentation.

## 🐛 Bug Fixes

- **Prompt cache embeddings never persisted** — Fixed serialization of embedding fields.
- **Chat cache replayed incorrect actions** — Prevented cross-action semantic collisions by splitting routing/chat thresholds.
- **Routing cache hit count not persisted** — Exact-hit `HitCount` now writes back to Redis.
- **Cache config changes required restart** — Migrated to `IOptionsMonitor<T>` for hot-reload.
- **Embedding provider concurrency issue** — Replaced unsafe dictionary usage with concurrency-safe coordination.
- **Tracing duration always reported 0ms** — Routing completion timing now measures elapsed request duration correctly.
- **Unsupported tool-chain config caused provider errors** — Removed invalid configuration path.
- **Dashboard dependency vulnerabilities** — Patched minimatch (CVE-2026-27903) and rollup (CVE-2026-27606).
- **Home Assistant follow-up flow regression** — Fixed continuity and follow-up behavior for multi-turn requests.
- **Plugin install regressions** — Fixed install path/tooling issues affecting plugin usability.
- **HACS validation workflow failure** — Corrected CI step order to ensure repository content is present.
- **Startup embedding failures from blank input** — Prevented invalid embedding calls caused by empty match names.
- **Embedding request bursts during cache reloads** — Moved generation to throttled background batching to reduce provider rate-limit pressure.
- **Presence sensor refresh duplicate-key crashes** — Fixed MongoDB duplicate key failures on re-scan by deduplicating auto-detected sensor IDs and skipping IDs already reserved by user overrides (issue #41).
- **Skill Optimizer "Import from Traces" broken** — Fixed hardcoded tool names (used deprecated `FindLightAsync` instead of `GetLightsState`), missing agent filter on trace queries, and parameter name mismatch (`searchTerm` vs `searchTerms` array). Trace import now uses skill-declared `SearchToolNames` and `AgentId`.
- **Music Agent playback regression** — Removed unnecessary `?return_response` query parameter from `play_media` service calls. Migrated Music Agent from custom entity resolution (Redis cache, embeddings, MA-specific filtering) to shared `IEntityLocationService`.
- **Stale entities in location cache** — Added full entity removal (not just embedding eviction) so duplicate/orphaned entities can be deleted from the cache.
- **Kubernetes HPA for single-instance app** — Removed HorizontalPodAutoscaler from helm chart and raw manifests since Lucia doesn't support multi-instance yet.

## 🧪 Testing

- **Playwright e2e test** — `PromptCacheRoutingTests` validates prompt-cache embedding round-trip and semantic route matching behavior.
- **Embedding diagnostics** — `EmbeddingMatchingTests` measure similarity behavior across synonym/opposite/cross-domain prompt variants.
- **Plugin/install regression coverage** — Added and updated tests around plugin installability and related dashboard/setup flows.
- **Name fallback tests** — `EntityMatchNameFormatterTests` validates alias sanitization and ID-based fallback behavior used by embedding inputs.
- **Brave Search contract tests** — `BraveSearchWebSearchSkillTests` validates HTTP request format, auth headers, JSON deserialization, empty results, and error handling.
- **Plugin config schema tests** — `PluginConfigSchemaTests` validates default interface members, schema declaration, property equality, and filtering logic.
- **Skill Optimizer trace import e2e** — `03-skill-optimizer-traces.spec.ts` validates traces API returns Light Agent data, skill traces API extracts search terms, and UI "Import from Traces" populates test cases.
- **Agent impersonation e2e** — `04-entity-location-impersonate.spec.ts` validates agent domain metadata, entity filtering by domain+agent (22 light/switch entities for light-agent), and search scoping ("zack's light" returns 1 result as light-agent).
- **Dashboard port pinned** — Vite dashboard pinned to port 7233 via Aspire `WithEndpoint` for stable Playwright test URLs.

## 📋 New Files

| Path | Purpose |
|------|---------|
| `lucia.Agents/Abstractions/IEntityLocationService.cs` | Centralized entity resolution interface |
| `lucia.Agents/Abstractions/IHybridEntityMatcher.cs` | Multi-signal entity search interface |
| `lucia.Agents/Abstractions/IMatchableEntity.cs` | Entity search participation contract |
| `lucia.Agents/Models/HomeAssistant/HomeAssistantEntity.cs` | Unified HA entity base class |
| `lucia.Agents/Models/HomeAssistant/SupportedColorModes.cs` | Light color mode bitflag enum |
| `lucia.Agents/Models/HomeAssistant/FloorInfo.cs` | Floor model with area containment |
| `lucia.Agents/Models/HomeAssistant/OccupiedArea.cs` | Area occupancy tracking |
| `lucia.Agents/Models/HomeAssistant/EntityVisibilityConfig.cs` | Per-entity visibility settings |
| `lucia.Agents/Models/HybridEntityMatcher.cs` | Multi-weighted entity matching engine |
| `lucia.Agents/Models/HybridMatchOptions.cs` | Matcher configuration (weights, thresholds) |
| `lucia.Agents/Models/MatchableEntityInfo.cs` | Searchable entity wrapper |
| `lucia.Agents/Models/EntityMatchResult.cs` | Scored match result with signal breakdown |
| `lucia.Agents/Models/HierarchicalSearchResult.cs` | Floor→Area→Entity search result |
| `lucia.Agents/Models/ResolutionStrategy.cs` | Entity resolution strategy enum |
| `lucia.Agents/Integration/SearchTermCache.cs` | Cached search term normalization |
| `lucia.Agents/Integration/SearchTermNormalizer.cs` | Query normalization pipeline |
| `lucia.HomeAssistant/Models/ExposedEntityListResponse.cs` | HA WebSocket exposed entity response |
| `lucia.AgentHost/Apis/EntityVisibilityApi.cs` | Entity visibility REST endpoints |
| `lucia.AgentHost/Apis/MatcherDebugApi.cs` | Matcher debug/testing REST endpoints |
| `lucia.Agents/Models/HomeAssistant/EntityLocationEmbeddingProgress.cs` | Embedding coverage/progress snapshot model |
| `lucia.Agents/Services/EntityMatchNameFormatter.cs` | Safe name/alias formatter with ID fallback |
| `lucia-dashboard/src/pages/MatcherDebugPage.tsx` | Matcher debug dashboard page |
| `lucia-dashboard/src/pages/EntityLocationPage.tsx` | Live embedding progress UI + embedding actions |
| `lucia.PlaywrightTests/Agents/PromptCacheRoutingTests.cs` | Cache embedding e2e test |
| `plugins/brave-search/plugin.cs` | Brave Search API web search plugin |
| `lucia.Agents/Abstractions/PluginConfigProperty.cs` | Plugin config property record type |
| `lucia.AgentHost/PluginFramework/Models/PluginConfigSchemaDto.cs` | Plugin config schema API DTO |
| `lucia.AgentHost/PluginFramework/Models/PluginConfigPropertyDto.cs` | Plugin config property API DTO |
| `lucia-dashboard/src/components/PluginConfigTab.tsx` | Plugin configuration tab component |
| `lucia.Tests/BraveSearchWebSearchSkillTests.cs` | Brave Search HTTP contract tests |
| `lucia.Tests/PluginConfigSchemaTests.cs` | Plugin config schema infrastructure tests |
| `lucia.Agents/Abstractions/ISkillConfigProvider.cs` | Agent skill config section exposure interface |
| `lucia.Agents/Abstractions/SkillConfigSection.cs` | Skill config section descriptor with OptionsType |
| `lucia.Agents/Configuration/SceneControlSkillOptions.cs` | Scene skill configurable options |
| `lucia.MusicAgent/MusicPlaybackSkillOptions.cs` | Music skill configurable options |
| `lucia.AgentHost/Apis/AgentInfo.cs` | Agent info DTO with domain metadata |
| `lucia-dashboard/src/components/EntityMultiSelect.tsx` | Multi-select with chips, search, keyboard nav |
| `lucia-dashboard/src/components/SkillConfigEditor.tsx` | Schema-driven skill config form editor |
| `lucia-playwright/e2e/03-skill-optimizer-traces.spec.ts` | Skill optimizer trace import e2e tests |
| `lucia-playwright/e2e/04-entity-location-impersonate.spec.ts` | Agent impersonation filter e2e tests |

## 🗑️ Removed / Deprecated

| Path | Reason |
|------|--------|
| `lucia.Agents/Services/IEntityLocationService.cs` | Moved to `Abstractions/` namespace |
| `lucia.Agents/Models/FloorInfo.cs` | Moved to `Models/HomeAssistant/` |
| `lucia.Agents/Models/OccupiedArea.cs` | Moved to `Models/HomeAssistant/` |
| `lucia.Agents/Agents/DiagnosticChatClientWrapper.cs` | Replaced by `TracingChatClientFactory` |
| `lucia.Agents/Configuration/AgentConfiguration.cs` | Replaced by `AgentDefinition` |
| `LightControlSkill.FindLightsByAreaAsync` | Throws `NotSupportedException` — use entity tools |
| `LightControlSkill.FindLightAsync` | Throws `NotSupportedException` — use entity tools |
| `IOptimizableSkill.GetCachedEntitiesAsync` | Removed — optimizer uses location service |
| `infra/kubernetes/manifests/07-hpa.yaml` | Removed — single-instance constraint, no HPA needed |
| `MusicPlaybackSkill` custom resolution | Replaced by `IEntityLocationService.SearchHierarchyAsync` |
| Music Agent A2A container | Moved in-process with AgentHost |

---

## ⬆️ Upgrade Notes

1. **Cache eviction recommended** — Evict routing and chat caches via dashboard (or `DELETE /api/prompt-cache` and `DELETE /api/chat-cache`) to clear stale entries.
2. **New cache config properties** — `RouterExecutor:SemanticSimilarityThreshold` (0.95) and `RouterExecutor:ChatCacheSemanticThreshold` (0.98) can be tuned in dashboard settings.
3. **Skill API breaking changes** — Skills calling `FindLightsByAreaAsync`, `FindLightAsync`, or `GetCachedEntitiesAsync` must migrate to unified entity tools via `EntityLocationService`.
4. **Music Agent now in-process** — The music-agent container is no longer deployed separately. Remove the `music-agent` Deployment/Service from your Kubernetes cluster if upgrading from mesh mode.
5. **HPA removed** — If you had a HorizontalPodAutoscaler for the `lucia` deployment, remove it. Lucia requires a single replica until HA support is implemented.
6. **Skill config auto-seeded** — New skill option sections (`LightControlSkill`, `ClimateControlSkill`, `FanControlSkill`, `SceneControlSkill`, `MusicPlaybackSkill`) are auto-seeded to MongoDB on first startup. Edit via Agent Definitions page, not the raw Configuration page.
7. **Entity cleanup** — Use the new delete button on Entity Locations to remove stale/duplicate entities that may cause service call failures.

---

# Release Notes - 2026.03.01

**Release Date:** March 1, 2026  
**Code Name:** "Nebula"

---

## 🌌 Overview

"Nebula" introduces a full plugin system to Lucia — a dynamic, script-based architecture that lets users extend the assistant with community and first-party plugins without recompiling. Like a nebula giving birth to new stars, this release provides the raw building blocks for an ecosystem of optional capabilities that can be discovered, installed, and managed at runtime. The first two plugins ship alongside the framework: **MetaMCP** (MCP tool aggregation) and **SearXNG** (privacy-first web search).

## 🚀 Highlights

- **Roslyn Script Plugin Engine** — Plugins are plain C# scripts (`plugin.cs`) evaluated at startup via Roslyn CSharpScript. No compilation step, no separate DLL projects — just drop a folder and go.
- **Four-Hook Plugin Lifecycle** — `ConfigureServices` → `ExecuteAsync` → `MapEndpoints` → `OnSystemReadyAsync` gives plugins precise control over when they inject DI services, run initialization, register API endpoints, or interact with fully-initialized agents.
- **Plugin Repository System** — Discover, install, enable/disable, and uninstall plugins from configurable repositories through a new dashboard UI and REST API.
- **Git Blob Source Strategies** — Git-based repositories support three download modes: GitHub Release assets (preferred), tag archives, and branch archives — with automatic fallback.
- **IWebSearchSkill Abstraction** — Web search is now an optional, pluggable capability. The built-in SearXNG implementation ships as a plugin; users without a search provider simply don't install it.

## ✨ What's New

### 🔌 Plugin Engine (`ILuciaPlugin`)

- **Script-based plugins** — Each plugin lives in `plugins/{name}/plugin.cs` and implements `ILuciaPlugin` via Roslyn scripting. No project files, no NuGet references — the host provides all standard assemblies.
- **Four lifecycle hooks:**
  - `ConfigureServices(IHostApplicationBuilder)` — Register DI services before the app is built (e.g., `IWebSearchSkill`).
  - `ExecuteAsync(IServiceProvider, CancellationToken)` — Run post-build initialization.
  - `MapEndpoints(WebApplication)` — Register custom HTTP endpoints.
  - `OnSystemReadyAsync(IServiceProvider, CancellationToken)` — Execute after all agents are online — ideal for agent-dependent logic.
- **Auto-discovery** — `PluginLoader` scans the plugin directory and evaluates all `plugin.cs` entry points at startup.
- **Safe evaluation** — `PluginScriptHost` provides a locked-down `ScriptOptions` with explicit assembly references and namespace imports.

### 📦 Plugin Repository & Store

- **Repository model** — Each repository is defined by a `lucia-plugins.json` manifest containing an `id`, `name`, and array of `plugins` with metadata (id, name, description, version, path, author, tags).
- **Two source implementations:**
  - `LocalPluginRepositorySource` — Reads manifests and copies plugins from a local directory. Used during development.
  - `GitPluginRepositorySource` — Fetches manifests from `raw.githubusercontent.com` and downloads plugin archives from GitHub. Used in production.
- **Blob source strategies** (git repos) — The `blobSource` field on a repository definition controls download behavior:
  - `release` (default) — Fetches the latest GitHub Release, looks for a per-plugin `{pluginId}.zip` asset, falls back to the release zipball, then to a branch archive.
  - `tag` — Downloads the archive at a specific tag ref.
  - `branch` — Downloads the archive at branch HEAD.
- **MongoDB persistence** — `PluginRepositoryDefinition` and `InstalledPluginRecord` are stored in MongoDB collections with full CRUD via `IPluginManagementRepository`.
- **Plugin change tracker** — `PluginChangeTracker` emits real-time change notifications consumed by the dashboard for live badge/count updates.

### 🖥️ Dashboard — Plugins Page

- **Three-tab layout:** Installed plugins, Plugin Store (available from repositories), and Repository management.
- **Install/Uninstall** — One-click install from the store; uninstall removes plugin files and database record.
- **Enable/Disable** — Toggle plugins without uninstalling. Requires app restart (surfaced via a restart banner).
- **Repository management** — Add/remove/sync plugin repositories. Add dialog supports both Local and Git repository types.
- **Live counters** — Tab badges update in real-time as plugins are installed or repositories are synced.
- **Restart banner** — A persistent banner appears when plugin changes require a restart, with a one-click restart button.

### 🔍 IWebSearchSkill Abstraction

- **Optional capability** — `GeneralAgent` accepts an optional `IWebSearchSkill?` parameter. When present, web search tools are registered; when absent, search is silently unavailable.
- **Clean extraction** — `WebSearchSkill.cs` and `SearXngOptions.cs` removed from core `lucia.Agents`. The SearXNG implementation now lives entirely in `plugins/searxng/plugin.cs`.
- **OpenTelemetry instrumented** — The SearXNG plugin includes `ActivitySource` tracing and `Meter` metrics for search operations, consistent with the core agent telemetry.

### 🔧 REST APIs

- **`/api/plugins/installed`** — List, enable/disable, uninstall installed plugins.
- **`/api/plugins/store`** — Browse available plugins from all configured repositories, install plugins.
- **`/api/plugins/repositories`** — CRUD for plugin repositories, trigger repository sync.
- **`/api/system/restart`** — Graceful application restart for applying plugin changes.

## 🐛 Bug Fixes

- **Setup wizard session key** — `SetupSeedExtensions` now generates `Auth:SessionSigningKey` before setting `Auth:SetupComplete = true`, preventing the wizard from breaking when environment-variable seeding is used.
- **Plugin script namespace** — Added `System.Net.Http` to default Roslyn script imports so plugins can reference `IHttpClientFactory` without explicit `using` directives.

## 🧪 Testing

- **17 plugin system tests** — Covers script evaluation, plugin loading, management service operations (sync, enable, disable, install, uninstall), manifest validation, `LocalPluginRepositorySource` filesystem operations, `ParseGitHubOwnerRepo` URL parsing, and all three git blob source strategies (release with per-plugin asset, release zipball fallback, release-to-branch fallback, branch archive, tag archive).
- **456 total tests passing** (excluding Playwright/Eval).

## 📋 New Files

| Path | Purpose |
|------|---------|
| `lucia.Agents/Abstractions/ILuciaPlugin.cs` | Four-hook plugin interface |
| `lucia.Agents/Abstractions/IPluginManagementRepository.cs` | Repository persistence abstraction |
| `lucia.Agents/Abstractions/IPluginRepositorySource.cs` | Source abstraction (local vs git) |
| `lucia.Agents/Abstractions/IWebSearchSkill.cs` | Optional web search capability |
| `lucia.Agents/Configuration/PluginManifest.cs` | Repository manifest model |
| `lucia.Agents/Configuration/PluginManifestEntry.cs` | Plugin entry in manifest |
| `lucia.Agents/Configuration/PluginRepositoryDefinition.cs` | MongoDB repository document |
| `lucia.Agents/Configuration/InstalledPluginRecord.cs` | MongoDB installed plugin document |
| `lucia.Agents/Extensions/PluginLoader.cs` | Plugin auto-discovery and loading |
| `lucia.Agents/Extensions/PluginScriptHost.cs` | Roslyn script evaluation host |
| `lucia.Agents/Services/PluginManagementService.cs` | Plugin orchestration service |
| `lucia.Agents/Services/MongoPluginManagementRepository.cs` | MongoDB implementation |
| `lucia.Agents/Services/LocalPluginRepositorySource.cs` | Filesystem repository source |
| `lucia.Agents/Services/GitPluginRepositorySource.cs` | GitHub repository source |
| `lucia.Agents/Services/GitHubRelease.cs` | GitHub Release API DTO |
| `lucia.Agents/Services/GitHubReleaseAsset.cs` | GitHub Release asset DTO |
| `lucia.Agents/Services/AvailablePlugin.cs` | Store plugin DTO |
| `lucia.Agents/Services/PluginChangeTracker.cs` | Real-time change notifications |
| `lucia.AgentHost/Extensions/PluginStoreApi.cs` | Store REST endpoints |
| `lucia.AgentHost/Extensions/PluginRepositoryApi.cs` | Repository REST endpoints |
| `lucia.AgentHost/Extensions/InstalledPluginApi.cs` | Installed plugin REST endpoints |
| `lucia.AgentHost/Extensions/SystemApi.cs` | System management endpoints |
| `lucia-dashboard/src/pages/PluginsPage.tsx` | Dashboard plugins page |
| `lucia-dashboard/src/components/PluginRepoDialog.tsx` | Repository add dialog |
| `lucia-dashboard/src/components/RestartBanner.tsx` | Restart notification banner |
| `plugins/metamcp/plugin.cs` | MetaMCP aggregation plugin |
| `plugins/searxng/plugin.cs` | SearXNG web search plugin |
| `lucia-plugins.json` | Official plugin repository manifest |
| `lucia.Tests/Plugins/PluginSystemTests.cs` | Plugin system test suite |
| `lucia.Tests/FakeHttpMessageHandler.cs` | HTTP test double |

## 🗑️ Removed Files

| Path | Reason |
|------|--------|
| `lucia.Agents/Skills/WebSearchSkill.cs` | Extracted to `plugins/searxng/plugin.cs` |
| `lucia.Agents/Configuration/SearXngOptions.cs` | Moved into SearXNG plugin |
| `lucia.Agents/Extensions/McpServerSeedExtensions.cs` | Replaced by MetaMCP plugin |
| `lucia.Agents/Configuration/PluginRegistryFile.cs` | Replaced by `IPluginRepositorySource` abstraction |

---

# Release Notes - 2026.02.27

**Release Date:** February 27, 2026  
**Code Name:** "Keystone"

---

## 🗝️ Overview

"Keystone" is a UX and resilience release that transforms Lucia's first-run experience into a fully guided, self-healing onboarding wizard. Named after the wedge-shaped stone at the crown of an arch that locks everything into place—Keystone ensures that the dashboard API key, AI provider configuration, and agent initialization all interlock seamlessly before the user ever touches Home Assistant. Once that keystone is set, the arch holds.

## 🚀 Highlights

- **Guided AI Provider Setup** — New wizard step lets users configure Chat and Embedding providers (OpenAI, Azure OpenAI with API Key or Default Credential) directly during onboarding, with live connectivity tests before proceeding.
- **Agent Status Gate** — After providers are configured, the wizard waits for all agents to come online with real-time health polling before presenting the Home Assistant integration instructions.
- **Setup Resume Flow** — If a user's browser session is lost mid-setup (after key generation but before completion), they can now re-authenticate with their dashboard key and continue where they left off instead of hitting a dead end.
- **Infinite Prompt Cache** — Agent-level prompt cache entries no longer expire after 4 hours; they persist until manually evicted via the dashboard, maximizing LLM cost savings.

## ✨ What's New

### 🧙 Setup Wizard — AI Provider Configuration (Step 4)

- **Provider type selection** — Choose between OpenAI and Azure OpenAI for both Chat and Embedding capabilities.
- **Azure authentication modes** — API Key and Azure Default Credential are both supported. Default Credential enables managed identity deployments without storing secrets.
- **Live provider test** — Each configured provider is validated with a real API call before it can be saved. Failed tests show the error message inline.
- **Provider cards** — Configured providers display as summary cards with model/deployment name and a remove option.
- **Capability badges** — Visual indicators show which capabilities (Chat, Embedding) are configured.

### ⏳ Setup Wizard — Agent Status Gate (Step 5)

- **Real-time agent health polling** — After AI providers are saved, the wizard polls `/api/agents/status` every 3 seconds until all agents report healthy.
- **Per-agent status cards** — Each agent shows a spinner (initializing), green check (ready), or red X (failed) with the agent description.
- **Automatic progression** — The Next button enables only when all agents are online, preventing the user from configuring Home Assistant before agents are ready to receive commands.

### 🔄 Setup Resume Flow

- **Session recovery** — If the dashboard key was already generated but setup wasn't completed, the wizard detects this state and presents a login form instead of a dead-end message.
- **Cookie-aware authentication** — The wizard checks three auth sources: key generated this session, resumed via login form, or existing session cookie from a previous visit.
- **Graceful degradation** — Users who lose their key must reset MongoDB storage and start fresh—no backdoor recovery mechanism that could be exploited.

### ♾️ Infinite Prompt Cache Retention

- **Removed 4-hour TTL** — `ChatCacheTtl` field and all `TimeSpan` parameters removed from `StringSetAsync` calls in `RedisPromptCacheService`.
- **Manual eviction only** — Cache entries persist in Redis indefinitely until evicted via the Prompt Cache dashboard page (per-entry or bulk "Clear All").
- **Cost optimization** — Long-running instances accumulate cache hits over time without periodic cold-start penalties.

## 🐛 Bug Fixes

- **Orchestrator URL registration** — `OrchestratorAgent` was registering the internal container address (e.g., `http://localhost:5000/agent`) as its A2A endpoint instead of a relative `/agent` path. External callers couldn't reach the agent through the reverse proxy. Fixed to use relative URL resolution.
- **Azure OpenAI embedding 404** — The API-key path for Azure OpenAI embeddings used the plain `OpenAI.Embeddings.EmbeddingClient` which sends requests to the wrong URL pattern. Azure OpenAI requires `AzureOpenAIClient` for the `/openai/deployments/{name}/embeddings?api-version=...` route. Fixed to use `AzureOpenAIClient` + `AzureKeyCredential` for both API key and Default Credential paths.
- **Empty agent catalog crash** — Home Assistant custom component raised `ConfigEntryNotReady` when the agent catalog was empty instead of gracefully showing no agents. Fixed to return an empty list.
- **A2A SDK dependency removed** — Custom component no longer depends on the experimental `a2a-sdk` Python package, resolving HACS validation failures and installation issues.
- **Playwright test stability** — Rewrote setup wizard E2E tests to handle the resume flow, added Agent Cache tab switching for prompt cache validation, and fixed column mapping for the new cache table layout.
- **StepIndicator overflow** — The 6-step wizard progress tracker overflowed its container on narrow viewports. Fixed with proper flex-wrap and sizing.

## 🧪 Testing

- **7 setup wizard E2E tests** — Covers redirect, welcome page, key generation + HA config, AI provider configuration, agent status gate, dashboard authentication, and setup endpoint lockdown.
- **5 prompt cache E2E tests** — Validates cache login/clear, miss recording, hit detection, different-prompt separation, and manual eviction through the dashboard UI.
- **Resume flow coverage** — Tests navigate through the wizard with a pre-existing dashboard key, exercising the resume login path.

## 📦 Files Changed

- 20 files changed, +1,201 / −528 lines across dashboard, backend, tests, and HA custom component.

---

# Release Notes - 2026.02.26

**Release Date:** February 26, 2026  
**Code Name:** "Scene"

---

## 🎬 Overview

"Scene" adds a new **Scene Agent** to the Lucia catalog for activating Home Assistant scenes via natural language.

## ✨ What's New

### 📽️ Scene Agent

- **SceneControlSkill** — New skill with `ListScenesAsync`, `FindScenesByAreaAsync`, and `ActivateSceneAsync` tools for Home Assistant scene control
- **SceneAgent** — Specialized agent for scene activation at `/a2a/scene-agent`; routes queries like "activate movie scene", "turn on night mode", "what scenes are in the living room?"
- **Catalog integration** — Scene agent appears in the agent catalog and is routable via the orchestrator for #scenes domain


---

# Release Notes - 2026.02.25

**Release Date:** February 25, 2026  
**Code Name:** "Bastion"

---

## 🏰 Overview

"Bastion" is a security and performance release that locks down the onboarding flow, hardens A2A mesh authentication, and introduces a two-tier prompt cache with full conversation-aware key hashing. Named after a fortified stronghold—Bastion ensures that Lucia's APIs are protected from unauthorized access while dramatically reducing redundant LLM calls through intelligent caching of agent planning decisions.

## 🚀 Highlights

- **Setup Endpoint Security** — Three-phase security model ensures onboarding APIs are only accessible when appropriate: anonymous key generation, authenticated configuration, then permanent lockdown after setup completes.
- **Agent Prompt Caching** — New `PromptCachingChatClient` decorator caches LLM planning decisions (tool selection + arguments) across all agents, with conversation-aware cache keys that include function call content and tool results to prevent stale responses.
- **A2A Mesh Auth Fix** — Helm chart now auto-generates `InternalAuth__Token` for agent-to-registry communication in mesh deployments.
- **Cache Management Dashboard** — Rewritten Prompt Cache page with Router/Agent tab switcher, per-namespace stats, and eviction controls. Activity page shows Router, Agent, and Combined cache hit rates.

## ✨ What's New

### 🔐 Setup Endpoint Security

- **Three-phase security model** — Phase 1 (pre-key): only `GET /api/setup/status` and `POST /api/setup/generate-dashboard-key` are anonymous. Phase 2 (post-key): all remaining setup endpoints require API key authentication. Phase 3 (post-complete): `OnboardingMiddleware` returns 403 for all `/api/setup/*` permanently.
- **Race-window elimination** — Direct MongoDB check via `ConfigStoreWriter` + volatile one-way latch closes the 5-second window between setup completion and `MongoConfigurationProvider` polling.
- **Health check exemption** — Health endpoints remain anonymous for container orchestrator probes.

### ⚡ Agent Prompt Caching

- **Two-tier cache architecture** — Routing cache (`lucia:prompt-cache:`) stores router decisions; agent cache (`lucia:chat-cache:`) stores LLM planning responses with 4-hour TTL. Same user prompt hitting different agents produces separate entries (system instructions hash differentiates).
- **Full conversation key hashing** — Cache keys include `FunctionCallContent` (tool name + arguments) and `FunctionResultContent` (result text), not just `message.Text`. Different tool results automatically produce different cache keys, preventing stale response replay.
- **Volatile field stripping** — Regex strips `timestamp`, `day_of_week`, and `id` fields from HA context before SHA256 hashing so identical intents produce the same key regardless of when they're sent.
- **Clean display prompts** — `ExtractLastUserText()` stores the human-readable user message as `NormalizedPrompt` instead of the raw cache key for dashboard display.

### 📊 Cache Management UI

- **Tabbed Prompt Cache page** — Router and Agent cache entries displayed in separate tabs with dedicated StatsBar (entries, hits, misses, hit rate) and per-entry eviction.
- **Bulk eviction** — "Clear All" button per cache namespace for cache invalidation.
- **Activity dashboard integration** — Summary cards show Router, Agent, and Combined cache hit rates in a 6-column grid layout.
- **Chat cache REST API** — New `/api/chat-cache/*` endpoint group: `GET /` (list entries), `GET /stats` (hit/miss/rate), `DELETE /{key}` (evict single), `DELETE /` (evict all).

### 🔗 A2A Mesh Authentication

- **Helm chart `InternalAuth__Token`** — New `internalAuthToken` field in `values.yaml` with auto-generation via `randAlphaNum 32` in `secret.yaml`, injected as environment variable into all agent pods.
- **Registry auth fix** — A2A agents no longer receive 401 when registering with the agent registry in mesh deployments.

### 🎭 Playwright E2E Tests

- **Prompt cache validation** — 5 serial test cases verifying cache hit/miss behavior, eviction, and stats accuracy through the dashboard UI.
- **Environment-driven auth** — Tests read `DASHBOARD_API_KEY` from `.env` file for login; `.env.template` provides placeholder for CI setup.

## 🐛 Bug Fixes

- **Stale cache responses** — `message.Text` only returns `TextContent`, missing `FunctionCallContent` and `FunctionResultContent`. All round-2+ conversations hashed to the same key regardless of tool results, causing the LLM's cached response from a previous run to be replayed with wrong state (e.g., "lights are off" when they're on). Fixed by iterating `message.Contents` and including all content types in the key.
- **0% cache hit rate** — HA context injection of `timestamp`, `day_of_week`, and `id` made every prompt unique. Stripped via regex before hashing.
- **NormalizedPrompt display** — Cache entries showed raw key format (`instructions:sha...\nuser:...\nassistant:\ntool:`) instead of the clean user message.
- **TS interface mismatch** — `ChatCacheEntry.functionCalls` used `arguments` field name but JSON serializes as `argumentsJson`; fixed TypeScript interface.
- **A2A 401 in mesh mode** — `InternalAuth__Token` was auto-generated by Aspire AppHost but never injected in Helm chart for Kubernetes deployments.
- **Setup race window** — 5-second gap between `POST /api/setup/complete` and `MongoConfigurationProvider` config poll allowed anonymous access to setup endpoints.

---

# Release Notes - 2026.02.25

**Release Date:** February 25, 2026  
**Code Name:** "Zenith"

---

## 🌄 Overview

"Zenith" is a major feature release that introduces a full alarm clock system, CRON-based scheduled tasks, room-level presence detection, and deployment hardening for both standalone and mesh topologies. Named after the highest point the sun reaches in the sky—Zenith represents the peak of Lucia's home automation capabilities, bringing autonomous scheduling, presence-aware actions, and rock-solid multi-process deployment.

## 🚀 Highlights

- **Scheduled Task System** — New `ScheduledTaskStore` / `ScheduledTaskExecutionService` providing CRON-based scheduling with MongoDB persistence and crash recovery. Supports Alarm, Timer, and AgentTask types with automatic re-scheduling on startup.
- **Alarm Clock System** — Full alarm clock implementation with CRON recurring schedules, volume ramping, presence-based speaker routing, voice dismissal/snooze, auto-dismiss timeout, sound library with file upload, and a dedicated dashboard page.
- **Presence Detection** — Auto-discovers motion/occupancy/mmWave radar sensors from Home Assistant, maps them to areas with confidence levels (Highest→Low), and provides room-level occupancy data for alarm routing and future automations.
- **Mesh Mode Hardened** — Six bug fixes addressing service registration, agent URL resolution, duplicate endpoint mapping, conditional MongoDB clients, and JSON deserialization in mesh (Aspire multi-process) deployments.
- **Resilient Initialization** — Agent startup waits for chat providers, HA credentials refresh dynamically via `IOptionsMonitor`, and entity caches auto-reload on reconnection.

## ✨ What's New

### 📅 Scheduled Task System

- **ScheduledTaskStore** — In-memory store for active scheduled tasks (alarms, timers, agent tasks) with type-based querying
- **ScheduledTaskExecutionService** — `BackgroundService` polling every second, dispatches expired tasks with `IServiceScope` per execution
- **ScheduledTaskRecoveryService** — Restores pending tasks from MongoDB on startup via `ScheduledTaskFactory`
- **CronScheduleService** — CRON expression parsing, validation, next-fire-at computation, and human-readable descriptions
- **AgentScheduledTask** — Scheduled tasks that invoke LLM agents with a prompt at a specified time
- **SchedulerSkill** — AI tool for scheduling deferred agent actions from natural language ("remind me to check the oven in 30 minutes")

### ⏰ Alarm Clock System

- **AlarmClock model** — Alarm definitions with CRON recurring schedules, target entity, sound selection, and volume ramp settings
- **AlarmScheduledTask** — Alarm execution with `media_player.play_media` (announce mode) and `assist_satellite.announce` TTS fallback
- **Volume ramping** — Gradual volume increase via `media_player.volume_set` from configurable start to end volume over a ramp duration
- **Voice dismissal** — `DismissAlarm` and `SnoozeAlarm` AI tools accept optional alarm ID; omitting auto-targets whichever alarm is currently ringing
- **Presence-based routing** — Alarms with `targetEntity=presence` resolve to the occupied room's media player at fire time
- **AlarmClockApi** — Full REST API for alarm CRUD, enable/disable, dismiss/snooze, sound library management
- **Alarm Clocks dashboard** — React page with CRON builder (presets: daily/weekdays/weekends/custom), sound management, volume ramp controls

### 🔊 Alarm Sound Upload

- **`POST /api/alarms/sounds/upload`** — New multipart form-data endpoint accepting an audio file, name, and isDefault flag
- **HA Media Proxy** — Uploaded files are sent to Home Assistant's media library at `/local/alarms/` via `UploadMediaAsync`
- **Upload tracking** — Sounds created via upload have `UploadedViaLucia=true`; delete operations clean up the corresponding HA media file (best-effort)
- **Dashboard upload UI** — Sound form has an Upload File / HA Media URI toggle with file picker and auto-name detection from filename
- **Upload badge** — Uploaded sounds display an "Uploaded" badge in the sounds table

### 📡 Presence Detection

- **IPresenceDetectionService** — Auto-discovers presence sensors from Home Assistant by entity pattern and device class
- **Confidence levels** — Sensors classified as Highest (mmWave target count), High (mmWave binary), Medium (motion), Low (occupancy)
- **IPresenceSensorRepository** — MongoDB persistence for sensor-to-area mappings with user override support
- **PresenceApi** — REST API for sensor CRUD, occupied areas query, re-scan, and global enable/disable
- **Presence dashboard** — React page with occupied areas summary, sensors grouped by area, confidence selector, and enable/disable toggle
- **Auto-scan on startup** — `AgentInitializationService` calls `RefreshSensorMappingsAsync()` after entity locations load; graceful failure on scan errors

### 🏠 Standalone/Mesh Deployment

- **Deployment mode flag** — `Deployment__Mode` environment variable controls topology: `standalone` (default) embeds all agents in AgentHost; `mesh` runs agents as separate A2AHost processes
- **Dual-mode agent hosting** — Timer and Music agents detect their hosting context and configure URLs, endpoints, and service registrations accordingly
- **Conditional service registration** — MongoDB clients and data-access services register based on available connection strings and deployment mode, preventing startup crashes

### 🔄 Resilience & Initialization

- **Dynamic credential refresh** — `HomeAssistantClient` uses `IOptionsMonitor<HomeAssistantOptions>` for live HA token/URL updates without restart
- **Entity cache auto-reload** — Location cache reloads automatically on HA WebSocket reconnection and on empty cache access
- **Chat provider readiness** — `AgentInitializationService` waits for the chat completion provider before initializing agents, preventing null-reference failures on cold start
- **Light agent fallback** — `FindLightsByArea` falls back to `EntityLocationService` when the embedding-based light cache is empty

### 📦 HA Media Integration

- **Media browse** — `BrowseMediaAsync` for navigating HA media library directories
- **Media upload** — `UploadMediaAsync` for pushing audio files to HA's local media source
- **Media delete** — `DeleteMediaAsync` via WebSocket command for cleanup on sound removal

## 🐛 Bug Fixes

### Mesh Mode (Aspire Multi-Process)

- **Alarm data services not registered** — `IAlarmClockRepository`, `CronScheduleService`, and `ScheduledTaskStore` were only registered inside the standalone code path; now registered in both modes so dashboard REST APIs work in mesh deployments
- **Agent URL resolution** — TimerAgent and MusicAgent checked `isStandalone` before `selfUrl`, causing A2AHost agents to ignore their assigned service URL; reordered to check `selfUrl` first
- **Duplicate agent-card endpoints** — TimerAgentPlugin and MusicAgentPlugin both mapped `/a2a/*` mirror routes that collided with `AgentDiscoveryExtension`, causing `AmbiguousMatchException`; removed mirror paths from plugins
- **Music-agent startup hang** — `AddMongoDBClient("luciatasks")` was unconditional in A2AHost, but music-agent doesn't receive that connection string; made registration conditional on connection string availability
- **PresenceConfidence JSON deserialization** — Dashboard sent enum values as strings ("High") but `System.Text.Json` defaulted to integer parsing; added `[JsonConverter(typeof(JsonStringEnumConverter))]` to the `PresenceConfidence` enum

### Other

- **Presence sensors not visible on startup** — Sensors required a manual UI refresh; now auto-discovered during initialization
- **Light agent area search** — `FindLightsByArea` now falls back to `EntityLocationService` when the embedding-based light cache is empty, fixing "No lights available" responses for area-based queries
- **Docker port binding** — AgentHost now binds to `0.0.0.0` instead of `localhost` for LAN access from other containers
- **Docker Compose .env dependency** — Removed `.env` file requirement; all configuration is inline in `docker-compose.yml`

---

# Release Notes - 2026.02.25

**Release Date:** February 25, 2026  
**Code Name:** "Aurora"

---

## 🌅 Overview

"Aurora" is a reliability and observability release that hardens Lucia's agent platform with deep entity intelligence, bulletproof timer lifecycle management, full OpenTelemetry coverage for LLM calls, and end-to-end Playwright testing across all three agent routing modes. Named after the aurora borealis—the northern lights that dance across the sky with precision and brilliance—this release brings the same level of visibility and coordination to Lucia's multi-agent orchestration.

## 🚀 Highlights

- **Entity Location Service** — New `IEntityLocationService` providing semantic entity resolution with floor, area, alias, and `supported_features` bitmask data. Agents can now find entities by natural language name, filter by capabilities (e.g., `Announce`), and resolve physical locations—all backed by a Redis-cached entity registry populated via HA Jinja templates.
- **Timer BackgroundService** — Complete timer lifecycle refactor: timers now run in a dedicated `TimerExecutionService` (BackgroundService) with a thread-safe `ActiveTimerStore` singleton, fully decoupled from the HTTP request lifecycle. Timers survive request completion and announce via resolved satellite entities with proper feature detection.
- **Full LLM Trace Visibility** — `UseOpenTelemetry()` wired into every agent's `ChatClientAgent` builder plus the `ModelProviderResolver`, emitting `gen_ai.*` spans for all LLM calls. The `Microsoft.Extensions.AI` activity source is now registered in ServiceDefaults, and Azure IMDS credential probe noise is filtered from traces.
- **Dynamic Agent Fix** — Fixed `NormalizeAgentKey` to prefer `agent.Id` over `agent.Name`, resolving the mismatch where dynamic agents (e.g., joke-agent) were registered under their display name instead of their machine name.
- **Per-Agent Error Metrics** — Error rate calculation in the Activity Dashboard now tracks errors per agent via a new MongoDB aggregation pipeline, replacing the incorrect global average.
- **Playwright E2E Tests** — New `AgentRoutingTests` covering all three agent dispatch modes: in-process (light-agent), remote A2A (timer-agent), and dynamic (joke-agent). Each test exercises the full stack through the dashboard UI.

## ✨ What's New

### 🗺️ Entity Location Service

- **IEntityLocationService** — Semantic entity resolution with floor/area/alias lookups
- **IEmbeddingSimilarityService** — Shared cosine similarity service extracted from per-skill implementations
- **SupportedFeaturesFlags** — `[Flags]` enum for HA `supported_features` bitmask (`Announce`, `StartConversation`)
- **EntityLocationInfo** — Now includes typed `SupportedFeaturesFlags` property for capability-based filtering
- **Jinja template enrichment** — `supported_features` fetched via `state_attr()` in the entity registry template
- **Redis-cached registry** — Floor, area, and entity data cached with configurable TTL

### ⏱️ Timer Agent Overhaul

- **ActiveTimerStore** — Thread-safe singleton with `ConcurrentDictionary` for active timer management
- **TimerExecutionService** — `BackgroundService` polling every 1 second, fully independent of HTTP request lifecycle
- **Satellite resolution** — `ResolveSatelliteEntityAsync` filters by `HasFlag(Announce)` with `OrderByDescending` feature tiebreaker to prefer physical satellites
- **TimerRecoveryService** — Now uses `ActiveTimerStore` for consistent state across recovery and new timers

### 📡 OpenTelemetry Improvements

- **UseOpenTelemetry()** on all agents — ClimateAgent, DynamicAgent, GeneralAgent, LightAgent all emit `gen_ai.*` spans
- **ModelProviderResolver** — Chat clients created with OpenTelemetry wrapping at the provider level
- **Microsoft.Extensions.AI activity source** — Registered in ServiceDefaults for LLM call trace visibility
- **IMDS noise filter** — Azure managed identity probes to `169.254.169.254` excluded from HTTP traces
- **Per-agent error tracking** — `TraceStats.ErrorsByAgent` dictionary with MongoDB aggregation pipeline

### 🧪 Playwright Integration Tests

- **AgentRoutingTests** — 3 tests validating all agent dispatch modes via the dashboard chat UI
- **TimerAgentA2ATests** — A2A round-trip validation for the timer agent
- **Error indicator assertions** — Tests verify responses don't contain error patterns (NOT AVAILABLE, CONNECTION REFUSED, etc.)

## 🐛 Bug Fixes

- **Dynamic agent "not available"** — `NormalizeAgentKey` changed from `agent.Name ?? agent.Id` to `agent.Id ?? agent.Name`, fixing display name vs. machine name mismatch
- **Same error rate for all agents** — ActivityApi now computes per-agent error rates instead of applying global average
- **A2A service discovery** — Remote agents now use `A2AClient` with Aspire service-discovery-enabled `HttpClient` instead of card URLs
- **Thread-safe entity caches** — `LightControlSkill`, `ClimateControlSkill`, `FanControlSkill` caches converted to `ConcurrentDictionary`
- **WebSocket floor/area registries** — HA client uses .NET 10 `WebSocketStream` for config registry endpoints
- **Mesh tool nodes disappearing** — Dashboard dynamically creates tool nodes on `toolCall` events and keeps them visible after requests complete
- **Trace persistence race condition** — Replaced `AsyncLocal` with `ConcurrentDictionary` for trace capture across workflow boundaries

---

# Release Notes - 2026.02.23

**Release Date:** February 23, 2026  
**Code Name:** "Solstice"

---

## ☀️ Overview

"Solstice" is a major platform release that builds on Galaxy's foundation with a fully redesigned management dashboard, a configurable **Model Provider** system supporting 7+ LLM backends, runtime **MCP Tool Servers** for dynamic tool integration, user-defined **Agent Definitions** with hot-reload, a real-time **Activity Dashboard** with live agent mesh visualization, a **Climate Agent** for HVAC and fan control, **GitHub Copilot SDK** integration as a first-class provider, and a sweeping mobile-first responsive overhaul of every dashboard page. With 216 files changed across 13,480+ insertions, Solstice transforms Lucia's management experience and makes the platform truly configurable at runtime without code changes.

## 🚀 Highlights

- **Live Activity Dashboard** — Real-time agent mesh visualization powered by React Flow and Server-Sent Events. Watch orchestration requests flow through agents with animated edges, state-colored nodes (🤖 agents, 🔧 tools), and live connection status. Summary cards show request counts, error rates, cache hit rates, and task completions at a glance.
- **Model Provider System** — MongoDB-backed provider configuration supporting OpenAI, Azure OpenAI, Azure AI Inference, Ollama, Anthropic, and Google Gemini. Connection testing, embedding provider resolution, and per-agent model assignment — all configurable from the dashboard without restarts. (GitHub Copilot SDK provider is WIP and disabled.)
- **MCP Tool Servers** — Runtime Model Context Protocol integration with stdio and HTTP/SSE transports. Add, connect, and discover tools from external MCP servers. Agents can reference specific tools by server ID and tool name for fine-grained capability assignment.
- **Agent Definitions** — Define custom agents from the dashboard with system instructions, tool assignments, model connections, and embedding providers. Built-in agents are seeded on startup; user-created agents hot-reload into the running system via `DynamicAgent` and `DynamicAgentLoader`.
- **Climate Agent** — New domain agent with `ClimateControlSkill` (HVAC modes, temperature, humidity) and `FanControlSkill` (speed, oscillation, direction) for comprehensive climate management via Home Assistant entities.
- **GitHub Copilot SDK Provider (WIP)** — Experimental Copilot integration via `CopilotClientLifecycleService` with CLI auto-start, model discovery, and native `AIAgent` creation through `client.AsAIAgent()`. Currently disabled pending further development — not functional or supported in this release.
- **Microsoft Agent Framework RC1** — Upgraded from preview to RC1 with breaking API changes: consolidated agent creation, updated executor patterns, and improved session management.
- **Mobile-First Dashboard Overhaul** — Every page redesigned for mobile with collapsible sidebar, responsive grids, touch-friendly controls, truncated URLs, stacked metadata on small screens, and tag wrapping fixes across Agent Definitions, Model Providers, MCP Servers, and Configuration pages.
- **Observatory Theme Refresh** — Refined dark theme with improved contrast, amber accent glow effects, glass-panel components with backdrop blur, and consistent styling across all 12+ dashboard pages.

## ✨ What's New

### 📊 Live Activity Dashboard

- **Real-time mesh graph** with React Flow (@xyflow/react) showing orchestrator → agent → tool topology
- **Custom AgentNode** components with state indicators: Processing Prompt (amber), Calling Tools (blue), Generating Response (green), Error (red), Idle (gray)
- **Animated edges** between nodes during active orchestration with directional arrows
- **🤖 emoji for agents**, **🔧 emoji for tools**, **🌐 badge for remote agents** in the mesh view
- **ActivityTimeline** component — scrollable feed of recent events with emoji icons, timestamps, and agent/tool names (capped at 100 events)
- **Summary cards** — Total Requests, Error Rate, Cache Hit Rate, Tasks Completed
- **Agent activity stats table** — per-agent request counts, error rates, and last activity
- **SSE ack pattern** — Server sends immediate `connected` event on stream open for reliable status display
- **useActivityStream hook** — EventSource connection management with exponential backoff reconnection (up to 10 retries, max 30s delay)
- Default landing page at `/`

### 🔌 Model Provider Configuration

- **6 supported providers**: OpenAI, Azure OpenAI, Azure AI Inference, Ollama, Anthropic, Google Gemini (GitHub Copilot SDK is WIP and disabled)
- **ModelProviderResolver** creates `IChatClient` and `IEmbeddingGenerator` instances from stored configs with OpenTelemetry wrapping
- **MongoModelProviderRepository** with CRUD operations and unique name indexing
- **Connection testing** for both chat and embedding endpoints from the dashboard
- **Per-agent model assignment** — each agent definition specifies its model connection and embedding provider
- **EmbeddingProviderResolver** — per-agent embedding provider support replacing the global `IEmbeddingGenerator`
- **ModelProviderSeedExtensions** — seed default providers on first run or upgrade
- **Dashboard page** at `/model-providers` with provider type icons, endpoint display, and inline editing

### 🛠️ MCP Tool Servers

- **McpToolRegistry** — manages concurrent MCP client connections with `ConcurrentDictionary` caching
- **Stdio and HTTP/SSE transports** — `CreateStdioTransport()` for local CLI tools, `CreateHttpTransport()` for remote servers
- **Tool discovery** — `ResolveToolsAsync()` resolves agent tool references to `AITool` instances at runtime
- **McpToolServerDefinition** — persisted in MongoDB with command, URL, headers, environment variables, and transport type
- **Connection lifecycle** — connect, disconnect, and status monitoring from the dashboard
- **Dashboard page** at `/mcp-servers` with server status indicators, tool counts, and connection controls
- **Dynamic agent integration** — `DynamicAgent` resolves MCP tools by server ID + tool name from agent definitions

### 🤖 Agent Definitions

- **AgentDefinition** model — Name, DisplayName, Description, Instructions, Tools (per-tool granularity), ModelConnectionName, EmbeddingProviderName, Enabled, IsBuiltIn, IsRemote, IsOrchestrator flags
- **MongoAgentDefinitionRepository** — dual collection management (`agent_definitions`, `mcp_tool_servers`) with unique name indexing
- **AgentDefinitionSeedExtensions** — seed built-in agents (General Assistant, Light Controller, Climate Controller, Orchestrator) on startup
- **DynamicAgent** — runtime-constructed agents from MongoDB definitions with MCP tool resolution and AIAgent caching
- **DynamicAgentLoader** — `IHostedService` that loads agent definitions, constructs `DynamicAgent` instances, and registers them with the agent registry
- **Hot-reload** via `/api/agent-definitions/rebuild` endpoint
- **Dashboard page** at `/agent-definitions` with tag pills for capabilities, inline editing, and tool assignment

### 🌡️ Climate Agent

- **ClimateAgent** (`lucia.Agents/Agents/ClimateAgent.cs`) — domain agent for HVAC and fan control
- **ClimateControlSkill** — 795 lines covering:
  - Set temperature, HVAC mode (heat, cool, auto, off, fan_only, dry)
  - Humidity target and preset modes
  - Auxiliary heater and swing mode control
  - Entity discovery via Home Assistant API
- **FanControlSkill** — 778 lines covering:
  - Fan speed percentage and named presets
  - Oscillation toggle and direction control
  - Entity discovery and state reporting
- **Embedding-powered entity matching** for natural-language device references

### 🐙 GitHub Copilot SDK Integration (WIP)

> **Note:** This integration is experimental and currently disabled. It is not functional or supported in this release. The infrastructure is in place for future development.

- **CopilotClientLifecycleService** — `IHostedService` managing shared `CopilotClient` lifecycle with CLI binary auto-start
- **CopilotConnectService** — handles Copilot connection establishment and authentication flow
- **Native AIAgent creation** — Copilot providers produce `AIAgent` directly via `client.AsAIAgent(SessionConfig)`, bypassing standard `ChatClient` pipeline
- **CopilotModelMetadata** — stores CLI model info and connection state
- **Provider hidden from UI** — Copilot provider auto-detected but not manually configurable

### 🔐 Internal Token Authentication

- **InternalTokenAuthenticationHandler** — validates platform-injected Bearer tokens for service-to-service communication between AgentHost and A2AHost
- **Token generation** — 32-character random secret generated by AppHost and injected via environment variables
- **Claims identity** with `auth_method: internal_token` for authorization decisions

### 🎨 Dashboard UI Overhaul

- **Observatory theme refinements** — improved contrast on buttons (text-void over text-light), amber glow effects, glass-panel components
- **Mobile-first responsive layouts** on all pages:
  - Collapsible sidebar with hamburger menu on mobile
  - Responsive grids: `grid-cols-1 sm:grid-cols-2 lg:grid-cols-4`
  - Tag pills wrap (not word-wrap) with `flex-wrap` on agent definition headers
  - Tags placed below headers on mobile edit views
  - URL truncation (30-40 chars with `...`) on model provider cards
  - Stacked metadata (ID, model name, endpoint) on separate lines for mobile
  - Touch-friendly button sizes and tap targets
- **New pages**: Activity Dashboard, Agent Definitions, Model Providers, MCP Servers
- **Restyled pages**: Agents, Configuration (mobile-first rewrite), Traces, Exports, Tasks, Prompt Cache, Login, Setup Wizard
- **Setup wizard** — all 4 steps (Welcome, Configure, Connect, Done) polished with consistent styling

## ⚡ Improvements

### Orchestration

- **CompositeOrchestratorObserver** — delegates to all registered observers (TraceCaptureObserver, LiveActivityObserver) for extensible pipeline instrumentation
- **LiveActivityChannel** — bounded channel (capacity 100, DropOldest) bridging pipeline events to SSE dashboard
- **LiveActivityObserver** — emits lifecycle events: request start, routing decisions, agent dispatch, agent complete/error, response aggregated
- **AgentTracingChatClient** — now emits tool-level events (toolCall/toolResult) by scanning response messages for `FunctionCallContent`/`FunctionResultContent`
- **Agent ID matching** — prefer agent `Id` over `Name` for invoker key matching
- **URI security** — filter agent URIs by HTTP scheme to prevent `file://` invocation
- **Lazy A2A mapping** — deferred agent card to A2A mapping with agent flags and definition migration

### API Endpoints

- **ActivityApi** (`/api/activity`) — `/live` SSE stream, `/mesh` topology, `/summary` stats, `/agent-stats` per-agent metrics
- **McpServerApi** (`/api/mcp-servers`) — full CRUD + `/tools` discovery, `/connect`, `/disconnect`, `/status`
- **ModelProviderApi** (`/api/model-providers`) — CRUD + `/test` connection, `/test-embedding`, `/copilot/connect`
- **AgentDefinitionApi** (`/api/agent-definitions`) — CRUD + `/rebuild` hot-reload
- **Enhanced TraceManagementApi** — related traces navigation, fan companion detection, enriched trace metadata

### Dashboard API Client

- **api.ts** — 50+ typed API functions organized by domain (traces, exports, config, auth, setup, API keys, prompt cache, tasks, MCP servers, agent definitions, model providers, activity)
- **types.ts** — 30+ TypeScript interfaces matching backend models
- **useActivityStream hook** — dedicated SSE connection management with state tracking

### Infrastructure

- **A2A deployment manifests** — Kubernetes deployment for A2A plugin host with health checks and service discovery
- **Helm chart A2A template** — `a2a-deployment.yaml` added to Helm chart
- **ConfigMap updates** — environment variables for internal auth token and A2A configuration
- **Docker Compose** — added Copilot CLI configuration support

## 🧪 Testing

- **LiveActivityChannelTests** (3 tests) — channel write/read behavior with DrainAsync pattern
- **LiveActivityObserverTests** (7 tests) — all 4 observer lifecycle hooks, message truncation
- **CompositeOrchestratorObserverTests** (5 tests) — multi-observer delegation
- **TraceCaptureObserverTests** — fire-and-forget async trace capture behavior
- **ModelProviderResolverTests** (431 lines) — provider creation for all 7 provider types, error handling, telemetry wrapping
- **InternalTokenAuthenticationHandlerTests** — token validation, missing headers, invalid tokens
- **StubEmbeddingProviderResolver** — test double for embedding provider tests
- **Updated EvalTestFixture** — aligned with new model provider and embedding resolver patterns
- **Total: 272 tests passing** (15 new tests added)

## 📦 Dependency Updates

| Package | Previous | Current |
|---------|----------|---------|
| Microsoft.Agents.* | 1.0.0-preview.260212.1 | 1.0.0-rc1 |
| Microsoft.Agents.AI.Hosting | 1.0.0-preview.260219.1 | 1.0.0-preview.260219.1 |
| Microsoft.Extensions.AI.* | 10.3.0 | 10.3.0 |
| Aspire.* | 13.1.1 | 13.1.1 |
| OpenTelemetry.* | 1.10.0 | 1.14.0 |
| Anthropic | — | 12.7.0 |
| ModelContextProtocol | — | 0.9.0-preview.1 |
| Microsoft.ML.Tokenizers | — | 2.0.0 |
| @xyflow/react | — | 12.10.1 |

## 🔨 Breaking Changes

- **Microsoft Agent Framework RC1** — upgraded from preview to RC1 with consolidated agent creation APIs
- **Legacy IChatClient DI removed** — all agents now use the model provider system; direct `IChatClient` injection is no longer supported
- **Global IEmbeddingGenerator removed** — replaced by per-agent `IEmbeddingProviderResolver`
- **Agent registration refactored** — agents implement `ILuciaAgent` and are auto-discovered via `IEnumerable<ILuciaAgent>` instead of concrete type injection

## 🐛 Bug Fixes

- Fixed button contrast on MCP Servers page (text-light → text-void)
- Fixed SSE connection showing "Disconnected" until first event (added ack event)
- Fixed tag pill word-wrapping on mobile — tags now flex-wrap as whole units
- Fixed edit form tag display on mobile — tags placed on separate line below header
- Fixed URL overflow on Model Providers mobile view — truncated with ellipsis
- Fixed metadata cramming on mobile — stacked on separate lines
- Fixed Configuration page mobile layout — complete mobile-first rewrite
- Fixed agent URI security — filter by HTTP scheme to prevent file:// invocation
- Fixed agent ID matching — prefer Id over Name for invoker key resolution
- Fixed DynamicAgentLoader singleton registration for DI resolution
- Fixed sync-over-async issues in MCP tool resolution
- Fixed dynamic agent unregistration from registry on delete
- Fixed JsonStringEnumConverter for ProviderType serialization

## 📝 Documentation

- **README.md** — complete dashboard section rewrite with 13 fresh screenshots at 1440×900
- **Setup wizard screenshots** — all 4 steps (Welcome, Configure, Connect, Done) captured
- **New page screenshots** — Activity Dashboard, Agent Definitions, Model Providers, MCP Servers
- **Refreshed existing screenshots** — Agents (with chat panel), Configuration, Traces, Exports, Tasks, Prompt Cache, Login
- **Spec 004** — Activity Dashboard specification and task list (`specs/004-activity-dashboard/`)

---
---

# Release Notes - 2026.02.20

**Release Date:** February 20, 2026  
**Code Name:** "Galaxy"

---

## 🌌 Overview

"Galaxy" is the largest release in Lucia's history — a sweeping upgrade that touches nearly every layer of the stack. At its core, this release migrates the entire platform to **Microsoft Agent Framework 1.0.0-preview.260212.1** and **.NET 10**, introduces a full-featured **React management dashboard**, a comprehensive **REST API** with 44+ endpoints, a complete **authentication and onboarding system**, an **LLM fine-tuning data pipeline** backed by **MongoDB**, a **prompt caching layer** for routing decisions, a brand-new **Timer Agent** for timed announcements, a separate **A2A plugin host**, production-ready **Kubernetes Helm charts** and **Docker Compose** deployments, and a comprehensive **evaluation testing framework** for measuring agent quality at scale. With 535 files changed across 86k+ lines, "Galaxy" transforms Lucia from a conversational assistant into a self-improving, observable, and extensible agentic platform.

## 🚀 Highlights

- **React Management Dashboard** — A brand-new React 19 + Vite 7 web UI with dark theme, 9 pages covering traces, agents, configuration, dataset exports, prompt cache, tasks, and a guided setup wizard. Built with TanStack Query and Tailwind CSS.
- **44+ REST API Endpoints** — Comprehensive API surface for trace management, dataset exports, prompt cache, task management, configuration, agent registry, A2A protocol, authentication, API key management, and onboarding setup.
- **Authentication & Onboarding** — API key authentication with HMAC-signed sessions, a 4-step setup wizard, and `OnboardingMiddleware` that gates all endpoints until initial configuration is complete.
- **Microsoft Agent Framework 1.0.0-preview.260212.1** — Full migration to the latest MAF preview with breaking API changes including `AgentThread` → `AgentSession`, `ChatMessageStore` → `ChatHistoryProvider`, and consolidated `AsAIAgent()` creation. Session management is now fully async, and source-generated executors replace reflection-based patterns.
- **Orchestrator Pipeline** — Three-stage executor pipeline (Router → Dispatch → Aggregator) with parallel agent invocation, prompt caching for routing decisions, and natural-language result aggregation.
- **Prompt Caching** — `PromptCachingChatClient` decorator caches router LLM routing decisions in Redis. Agents still execute tools fresh — only the routing step is cached. Full cache management UI with hit rate stats and eviction controls.
- **A2A Plugin Host** — Separate `lucia.A2AHost` service hosts agent plugins (Music Agent, Timer Agent) with auto-registration, health polling, and HA config polling every 10 seconds.
- **LLM Fine-Tuning Data Pipeline** — A production-grade training system that automatically captures orchestrator and agent conversation traces, stores them in MongoDB, and exports them as OpenAI-compatible JSONL datasets. Includes sensitive data redaction, labeling workflows, configurable retention, and per-agent filtering.
- **MongoDB Integration** — Dual-purpose MongoDB backend for trace/training data storage (`luciatraces`) and hot-reloadable application configuration (`luciaconfig`). Configuration changes poll every 5 seconds and override appsettings.json without restarts.
- **Timer Agent** — New agent for creating timed announcements and reminders on Home Assistant assist satellite devices. Supports natural-language duration parsing, concurrent timer management, and TTS announcements via `assist_satellite.announce`.
- **Task Persistence & Management** — Active tasks in Redis, archived tasks in MongoDB, with automatic archival via `TaskArchivalService`. Dashboard shows active/archived tasks with search, filtering, and cancellation.
- **Kubernetes Helm Charts** — Production-ready Helm chart (v1.0.0) for Kubernetes ≥1.24 with Redis and MongoDB StatefulSets, init container dependency waiting, rolling updates, pod security contexts, and Ingress support.
- **Docker Compose Deployment** — Hardened multi-service Docker Compose with Redis, MongoDB, and Lucia containers featuring read-only filesystems, dropped capabilities, resource limits, health checks, and log rotation.
- **Evaluation Testing Framework** — Comprehensive eval tests using `Microsoft.Extensions.AI.Evaluation` with LLM-based evaluators (Relevance, Coherence, ToolCallAccuracy, TaskAdherence, Latency). Cross-products models × prompt variants with disk-based reporting via `dotnet aieval report`.
- **CI/CD Hardening** — Explicit permissions blocks on all workflows, multi-platform Docker builds (amd64/arm64), Trivy security scanning, Helm linting with kubeval, and infrastructure validation.

## ✨ What's New

### 🤖 Timer Agent & Skill

- **SetTimer**: Create timers with natural-language durations ("5 minutes", "1 hour 30 minutes") targeting specific satellite devices
- **CancelTimer**: Cancel active timers by ID with graceful cleanup
- **ListTimers**: View all active timers with remaining time and messages
- Thread-safe concurrent timer tracking via `ConcurrentDictionary`
- Fire-and-forget execution model with `CancellationTokenSource` support
- OpenTelemetry instrumentation for timer operations
- Testable time handling via `TimeProvider`

### 🧠 LLM Fine-Tuning Pipeline

- **TraceCaptureObserver**: Hooks into orchestrator lifecycle to capture full interaction traces without impacting latency (fire-and-forget)
- **AgentTracingChatClient**: Wrapping `DelegatingChatClient` that captures system prompts, user messages, tool calls, tool results, and assistant responses per-agent
- **AsyncLocal session correlation**: Links orchestrator and per-agent traces together across async boundaries
- **JsonlConverter**: Exports traces to OpenAI fine-tuning JSONL format with human correction labels, per-agent filtering, and tool call inclusion
- **TraceRetentionService**: Auto-cleans unlabeled traces after configurable retention period (default 30 days)
- **Sensitive data redaction**: Configurable regex patterns strip API keys, tokens, and JWTs before persistence

### 🗄️ MongoDB Backend

- **Trace Storage** (`MongoTraceRepository`): CRUD operations with automatic indexing on Timestamp, Label.Status, and AgentId fields
- **Configuration Storage** (`MongoConfigurationProvider`): ASP.NET Core `IConfigurationProvider` backed by MongoDB with 5-second polling for hot-reload
- **ConfigSeeder**: Bootstraps MongoDB config from appsettings.json on first run
- **Stats aggregation**: Total, labeled, unlabeled, and errored trace counts by agent
- **Filtering**: Query traces by date range, agent, model, label status, and keyword

### 📊 Evaluation Testing

- **EvalTestFixture** & **AgentEvalTestBase**: Reusable infrastructure for agent quality testing
- **Real agent instances** backed by Azure OpenAI evaluation models with `FunctionInvokingChatClient` for actual tool execution
- **LLM-based evaluators**: Relevance, Coherence, ToolCallAccuracy, TaskAdherence, and Latency scoring
- **Cross-product parameterization**: `[MemberData]` drives model × prompt variant test matrices
- **Eval test suites**: LightAgentEvalTests, MusicAgentEvalTests, OrchestratorEvalTests with STT artifact variants and edge cases
- **ChatHistoryCapture** for recording intermediate tool calls and responses

### 🖥️ React Management Dashboard

A full-featured React 19 + Vite 7 web dashboard with dark theme, TanStack Query for data fetching, and Tailwind CSS styling. Nine pages provide complete visibility and control:

- **Setup Wizard**: Guided 4-step onboarding (Welcome → Configure Lucia → Connect HA Plugin → Done) with API key generation, HA connection testing, and plugin validation
- **Login**: API key authentication with session persistence
- **Traces**: Browse conversation traces with filtering by agent, date range, label status, and keyword search. Drill into individual traces to view agent execution records, tool calls, and responses. Label traces for fine-tuning with status, correction text, and reviewer notes
- **Agents**: Agent discovery and registration dashboard. Register agents by URL, view capabilities and skills, send test A2A messages, refresh metadata, and unregister agents
- **Configuration**: Live configuration management backed by MongoDB. Schema-driven form UI, Music Assistant integration testing, section-based organization with secret masking
- **Dataset Exports**: Generate OpenAI-compatible JSONL files for LLM fine-tuning with date range, agent, and label filters. Download generated datasets
- **Prompt Cache**: View all cached routing decisions with hit rate statistics, hit counts, and timestamps. Evict individual entries or clear entire cache
- **Tasks**: Active and archived task management with search, status filtering, pagination, and cancellation. View task statistics and details
- **Auth Context**: Protected routing with automatic redirect to setup wizard (if not configured) or login page (if not authenticated)

### 🔐 Authentication & API Key Management

- **API Key Authentication**: `ApiKeyAuthenticationHandler` validates keys against MongoDB-stored hashes with ASP.NET Core authentication pipeline integration
- **HMAC Session Service**: `HmacSessionService` creates cryptographically signed session cookies for browser-based dashboard access
- **API Key Lifecycle**: Create, list, revoke, and regenerate API keys with metadata (name, prefix, scopes, created/last-used/expiry timestamps)
- **Onboarding Middleware**: `OnboardingMiddleware` blocks all non-setup endpoints until initial configuration is complete. Setup endpoints use `[AllowAnonymous]`
- **Setup Flow**: 4-step wizard generates dashboard API key, configures Home Assistant connection (base URL + access token), tests connectivity, generates HA integration key, validates plugin presence, and marks setup complete

### 🔄 Orchestrator Pipeline

The multi-agent orchestration system has been decomposed into a clean three-stage executor pipeline:

- **RouterExecutor**: Routes user input to appropriate agents using LLM-based decision making with prompt cache integration. Returns cached routing decisions when available, bypassing the LLM call entirely
- **AgentDispatchExecutor**: Invokes selected agents in parallel using observer pattern for trace capture. Supports both local (in-process via `ILuciaAgent`) and remote (HTTP via A2A protocol) agent invocation
- **ResultAggregatorExecutor**: Aggregates responses from multiple agents into a single natural-language message. Orders by priority, handles partial failures with detailed reason reporting
- **LuciaEngine**: Decomposed from monolithic god class into focused services — delegates to `SessionManager` (session/task persistence) and `WorkflowFactory` (agent resolution + workflow execution)
- **SessionManager**: Manages session lifecycle with Redis-backed persistence
- **WorkflowFactory**: Creates workflow instances and resolves agent references

### 🔌 A2A Plugin Host

A separate `lucia.A2AHost` service that hosts agent plugins independently from the main AgentHost:

- **AgentHostService**: `IHostedLifecycleService` that initializes agents with 3-retry logic, polls HA config every 10 seconds, and registers agents via `AgentRegistryClient`
- **AgentRegistryClient**: HTTP client for registering/unregistering agents at the main AgentHost's `/agents/register` and `/agents/{agentId}` endpoints
- **Plugin Architecture**: `PluginLoader` and `PluginEndpointMappingExtension` for loading and mapping agent plugins
- **Agent Plugins**: Music Agent and Timer Agent run as separate A2A endpoints, each with their own Dockerfile for independent scaling

### ⚡ Prompt Caching

- **PromptCachingChatClient**: `DelegatingChatClient` decorator that checks Redis cache before forwarding LLM calls. Designed specifically for the RouterExecutor — caches routing decisions only, so agents always execute tools fresh
- **Cache-Aware Routing**: When a cache hit occurs, the RouterExecutor returns the cached agent selection without invoking the LLM, while agent dispatch and result aggregation still execute normally
- **Management API**: List entries, view statistics (total entries, hit rate, total hits/misses), evict individual entries, and clear all entries
- **Dashboard UI**: Full cache visibility with entry metadata (agent, confidence, reasoning, hit count, created/last-hit timestamps)

### 📋 Task Persistence & Archival

- **RedisTaskStore**: Active task persistence in Redis with TTL-based expiry
- **MongoTaskArchiveStore**: Historical task archival to MongoDB for long-term storage
- **ArchivingTaskStore**: Composite store that wraps both Redis and MongoDB stores with automatic archival
- **TaskArchivalService**: Background service that auto-archives completed tasks with configurable retention
- **Management API**: List active/archived tasks, view details, cancel tasks, and get task statistics
- **Dashboard UI**: Task management page with search, status filtering, and pagination

### 🌐 REST API Surface (44+ Endpoints)

| Category | Endpoints | Description |
|----------|-----------|-------------|
| Traces | 5 | List, detail, label, delete, statistics |
| Exports | 4 | Create JSONL, list, detail, download |
| Prompt Cache | 4 | List entries, stats, evict, clear all |
| Tasks | 5 | Active, archived, detail, cancel, stats |
| Configuration | 5 | List sections, get/update section, reset, schema |
| Auth | 3 | Login, logout, status |
| API Keys | 4 | List, create, revoke, regenerate |
| Setup | 8 | Status, generate keys, configure HA, test connection, validate, complete |
| Agents | 5 | List, register, update, unregister, proxy A2A |
| Discovery | 1 | `/.well-known/agent-card.json` |

### ☸️ Kubernetes Helm Charts

Production-ready Helm chart (v1.0.0, Kubernetes ≥1.24) with Artifact Hub annotations:

- **Multi-Service Deployment**: Separate deployments for AgentHost, A2A agents (Music, Timer), Redis StatefulSet, and MongoDB StatefulSet
- **Init Containers**: Dependency waiting pattern — services wait for Redis and MongoDB health before starting
- **Rolling Updates**: `maxSurge: 1`, `maxUnavailable: 0` for zero-downtime deployments
- **Pod Security**: Non-root user, read-only filesystem, `cap_drop: ALL`, `runAsNonRoot: true`
- **ConfigMap/Secret Checksums**: Automatic pod restarts when configuration changes
- **Liveness/Readiness Probes**: HTTP health check integration
- **Ingress Support**: Configurable Kubernetes Ingress with TLS
- **Values Files**: Production defaults (`values.yaml`) and development overrides (`values.dev.yaml`)

### 🐳 Docker Compose Deployment

Hardened multi-service Docker Compose deployment (`infra/docker/`):

- **Services**: Redis 8.2 (AOF persistence, 256MB maxmemory), MongoDB 8.0, Lucia AgentHost, and A2A agent plugin containers
- **Security**: Localhost-only port binding, read-only filesystems with tmpfs for temp dirs, dropped capabilities (`NET_RAW`, `SYS_PTRACE`, `SYS_ADMIN`), `no-new-privileges`
- **Resource Limits**: CPU and memory limits per container (Redis: 1CPU/512MB, MongoDB: 1CPU/512MB, Lucia: 2CPU/1GB)
- **Health Checks**: All services include health checks with automatic restart on failure
- **Logging**: JSON file driver with rotation (10MB max, 5 files)
- **Multi-Image Dockerfiles**: Separate Dockerfiles for AgentHost, A2AHost base, Music Agent, and Timer Agent
- **Documentation**: Deployment guide, testing guide, testing checklist, and Redis design rationale

### 🔧 CI/CD Improvements

- **Explicit Permissions**: All GitHub Actions workflows now have top-level `permissions` blocks with least-privilege scopes
- **Multi-Platform Docker Builds**: `docker-build-push.yml` builds for `linux/amd64` and `linux/arm64` with Docker Hub push, layer caching, artifact attestation, and Trivy security scanning
- **Helm Validation**: `helm-lint.yml` performs chart linting, template rendering, and schema validation with kubeval
- **Infrastructure Validation**: `validate-infrastructure.yml` validates Docker Compose, Kubernetes manifests (yamllint), systemd units, documentation (markdownlint), and runs security checks
- **Normalized Line Endings**: All infrastructure files converted to LF to prevent CRLF-related CI failures

## 🔧 Under the Hood

### MAF Migration (Breaking Changes)

| Before | After |
|--------|-------|
| `AgentThread` | `AgentSession` |
| `ChatMessageStore` | `ChatHistoryProvider` |
| `GetNewThread()` | `await CreateSessionAsync()` |
| `DeserializeThread()` | `await DeserializeSessionAsync()` |
| `session.Serialize()` | `agent.SerializeSession()` |
| `AgentRunResponse` | `AgentResponse` |
| `CreateAIAgent()` / `GetAIAgent()` | `AsAIAgent()` |
| `ReflectingExecutor` | Source-generated `[AgentTools]` |

- `DelegatingAIAgent` is now abstract — implementations must override core methods
- `AIAgent.Id` is now non-nullable (required)
- Provider signatures changed: `ChatHistoryProvider` and `AIContextProvider` now receive `(AIAgent agent, AgentSession session)`
- Custom `IAgentRegistry` replaces removed `AgentCatalog` for agent discovery

### Dependency Upgrades

- **Microsoft.Agents.AI** packages → **1.0.0-preview.260212.1**
- **Microsoft.Extensions.AI** → **10.2.0 / 10.3.0**
- Target framework → **.NET 10**

### DI Registration Fix

- Fixed `AddKeyedAzureOpenAIClient` poisoning the non-keyed `AzureOpenAIClient` registration with a null factory
- Keyed chat clients now reuse the shared non-keyed `OpenAIClient` to prevent double-registration conflicts

### APM & Observability

- **Custom meters**: `Lucia.TraceCapture`, `Lucia.Skills.LightControl`, `Lucia.Skills.MusicPlayback`
- **Activity sources**: Full tracing for Lucia orchestration, agents, skills, and all MAF/A2A namespaces
- **Health check filtering**: Excludes `/health` and `/alive` from trace recording
- **HTTP enrichment**: Request/response headers and bodies captured in OpenTelemetry spans

### Agent Registration & Discovery

- Enhanced agent registration with `IAgentRegistry` interface and `LocalAgentRegistry`
- Agent Cards (A2A protocol) now required for registration with full capability metadata
- `OrchestratorServiceKeys` manages agent-specific model keys for bulk registration
- Async initialization support added to MusicAgent
- `AgentInitializationService` now uses `IEnumerable<ILuciaAgent>` for auto-discovery instead of injecting each agent by concrete type
- `OrchestratorAgent` merges the old agent + AI agent classes, extends `AIAgent` and implements `ILuciaAgent`

### Orchestration Decomposition

- **LuciaEngine** decomposed from monolithic god class into 3 focused services with 6 constructor parameters (down from 16)
- **SessionManager**: Session lifecycle and task persistence
- **WorkflowFactory**: Agent resolution and workflow execution
- All 4 executors migrated from deprecated `ReflectingExecutor<T>` + `IMessageHandler<TIn,TOut>` to `Executor` base class with `ConfigureRoutes(RouteBuilder)` using `AddHandler<TIn,TOut>()`
- **RemoteAgentInvoker** and **LocalAgentInvoker** for A2A protocol and in-process agent invocation respectively
- In-process agents resolved via `GetAIAgent()` from DI (not `AsAIAgent()` which requires absolute URIs)

### Session & Cache Services

- **RedisSessionCacheService**: Session persistence backed by Redis
- **RedisPromptCacheService**: Prompt cache backed by Redis with TTL-based expiry
- **RedisDeviceCacheService**: Home Assistant device list caching
- **PromptCachingChatClient**: DelegatingChatClient decorator for router LLM call caching
- **ContextExtractor**: Extracts contextual data for agent prompting

### Housekeeping

- Removed obsolete multi-agent orchestration specification files
- Refactored `ContextExtractorTests` to use async mock registry creation
- Removed unused test doubles and updated package references

### Home Assistant Custom Component

- **Migrated to `_async_handle_message`**: Replaced deprecated `async_process` method with the new `_async_handle_message` API, adopting Home Assistant's built-in `ChatLog` for multi-turn conversation support
- **Chat log integration**: All agent responses (success and error) are now recorded in the HA `ChatLog` via `async_add_assistant_content_without_tools` with `AssistantContent`
- **`continue_conversation` support**: `ConversationResult` now includes the `continue_conversation` flag for future multi-turn flows
- Bumped component version to `2026.02.20`

## ✅ Upgrade Notes

- **Breaking**: This release requires **.NET 10** — upgrade your SDK before building.
- **Breaking**: MAF API changes require updating all agent session management code (see migration table above).
- **MongoDB required**: New trace capture and configuration features require MongoDB. Add MongoDB to your Aspire AppHost or provide connection strings for `luciatraces` and `luciaconfig` databases.
- **New environment variables**: `HA_ENDPOINT` and `HA_TOKEN` provide Home Assistant API access for testing and snapshot export.
- Existing installations should reload the integration after updating to register the new Timer Agent card.
- Eval tests require Azure OpenAI credentials configured for evaluation model access.
- **Home Assistant plugin**: The conversation entity now uses `_async_handle_message` instead of `async_process`. This is backwards-compatible per the HA team, but requires a recent Home Assistant Core version with `ChatLog` support.

## 🔮 What's Next

See our [Roadmap](https://github.com/seiggy/lucia-dotnet/blob/master/.docs/product/roadmap.md) for upcoming features:

- ~~**Climate Agent** — HVAC and temperature control~~ *(shipped in Solstice)*
- **Security Agent** — Alarms, locks, and camera integration
- ~~**Scene Agent** — Scene management and automation~~ *(shipped in Solstice)*
- **WebSocket Streaming** — Real-time Home Assistant event monitoring
- **Local LLM Fine-Tuning** — Use captured training data with local models for privacy-first deployment
- **Training UI Enhancements** — Batch labeling, inter-annotator agreement, and quality dashboards

---

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

---

# Release Notes - v2025.10.07

**Release Date:** October 7, 2025  
**Code Name:** "Illumination"

---

## 🌟 Overview

This release represents a major milestone for Lucia, bringing the autonomous home assistant from concept to working reality. Named after the Nordic sun goddess who brings light through darkness, this release illuminates your smart home with AI-powered agent automation.

> Correction (2026-02-20): references in this historical section to `lucia.HomeAssistant.SourceGenerator` reflect the 2025.10 state. Current runtime uses the hand-written `lucia.HomeAssistant/Services/HomeAssistantClient.cs` implementation.

## ✨ What's New

### 🏠 Home Assistant Integration (Complete)

#### Custom Component
- **Full Integration**: Native Home Assistant custom component with conversation platform
- **HACS Support**: Easy installation via Home Assistant Community Store
  - Add as custom repository: `https://github.com/seiggy/lucia-dotnet`
  - One-click download and installation
  - Automatic updates through HACS
- **Agent Selection UI**: Dynamic dropdown to choose between specialized agents
  - Automatically discovers available agents from catalog
  - Live agent switching without integration reload
  - Descriptive agent names with capability information
- **Configuration Flow**: Complete setup wizard with validation
  - Repository URL configuration
  - API key authentication (optional)
  - System prompt customization with Home Assistant template syntax
  - Max token configuration (10-4000 tokens)
  
#### Conversation Platform
- **Natural Language Processing**: Full integration with Home Assistant's conversation system
- **Intent Response**: Proper `IntentResponse` implementation for speech output
- **Context Threading**: Conversation continuity using `contextId` for multi-turn dialogues
- **Error Handling**: Graceful error messages with proper fallback behavior

#### Communication Protocol
- **JSON-RPC 2.0**: Standardized communication with agents
- **A2A Protocol v0.3.0**: Agent-to-Agent protocol implementation
- **TaskId Management**: Correct handling of `taskId: null` (required by Agent Framework)
- **Message Threading**: UUID-based message and context tracking
- **HTTP Client**: Async HTTP communication using `httpx` library

### 🤖 Agent System

#### Agent Framework (Microsoft Public Preview)
- **LightAgent**: Fully functional light and switch control
  - Semantic search for finding lights by natural language
  - Device capability detection (brightness, color temp, color modes)
  - State queries and control operations
  - Switch entity support (light switches)
  - Embedding-based similarity matching
  
- **MusicAgent**: Music Assistant integration
  - Playback control (play, pause, stop, skip)
  - Volume management
  - Queue management
  - Player discovery and selection
  - Music Assistant API integration

#### Agent Registry
- **Dynamic Discovery**: Agents register and expose capabilities
- **Catalog Endpoint**: `/agents` returns all available agents with metadata
- **Agent Cards**: Complete agent information including:
  - Name, description, version
  - Supported skills and capabilities
  - Protocol version
  - Example queries
  - Input/output modes

#### Skills System
- **LightControlSkill**: Comprehensive light control
  - `find_light`: Natural language light discovery using embeddings
  - `get_light_state`: Query current light status
  - `set_light_state`: Control on/off, brightness, color
  - Entity caching with 30-minute refresh
  - Cosine similarity matching for semantic search
  
- **MusicPlaybackSkill**: Music Assistant control
  - Playback operations
  - Volume control
  - Queue management
  - Player management

### 🏗️ Technical Infrastructure

#### .NET 10 RTM
- **Latest Framework**: Running on .NET 10 RTM
- **Modern C#**: Using C# 13 features and nullable reference types
- **Performance**: Optimized async/await patterns throughout

#### Agent Framework Integration
- **Microsoft Agent Framework**: Public Preview integration
- **ChatClientAgent**: Modern agent architecture
- **AIFunctionFactory**: Tool creation and registration
- **IChatClient**: LLM provider abstraction
- **IEmbeddingGenerator**: Embedding generation for semantic search

#### .NET Aspire
- **Cloud-Native**: .NET Aspire orchestration for development
- **Service Discovery**: Automatic service registration
- **Health Checks**: Built-in health monitoring
- **OpenTelemetry**: Distributed tracing and metrics

#### Multi-LLM Support
- **OpenAI**: Full GPT-4o and embedding support
- **Azure OpenAI**: Enterprise-grade Azure integration
- **Azure AI Inference**: Azure AI Studio models
- **Ollama**: Local LLM support for privacy-focused deployments
- **Connection Strings**: Standardized configuration format
- **Keyed Clients**: Multiple LLM configurations per application

### 🔧 Developer Experience

#### Project Structure
- **lucia.AgentHost**: Main agent hosting API (ASP.NET Core)
- **lucia.Agents**: Agent implementations and skills
- **lucia.HomeAssistant**: Home Assistant API client library
- **Home Assistant client generation**: Historical at this release point; now replaced by hand-written client implementation
- **lucia.AppHost**: .NET Aspire orchestration
- **lucia.ServiceDefaults**: Shared service configurations
- **lucia.Tests**: Comprehensive test suite
- **custom_components/lucia**: Home Assistant Python integration

#### Code Quality
- **Home Assistant client**: Hand-written, type-safe API implementation
- **Dependency Injection**: Full DI throughout the application
- **Async/Await**: Proper async patterns everywhere
- **Error Handling**: Comprehensive exception handling with logging
- **Logging**: Structured logging with Microsoft.Extensions.Logging

#### Testing Infrastructure
- **Unit Tests**: Agent and skill testing
- **Integration Tests**: End-to-end Home Assistant integration
- **Test Scripts**: Python test utilities for JSON-RPC validation
  - `test_catalog_simple.py`: Agent catalog and messaging tests

## 🎯 What Works

### ✅ Fully Functional Features

1. **Light Control**
   - Find lights using natural language ("living room light", "kitchen ceiling")
   - Turn lights on/off
   - Set brightness (0-100%)
   - Set colors by name
   - Query light status
   - Switch entity support

2. **Music Control**
   - Play/pause/stop playback
   - Volume control
   - Skip tracks
   - Queue management
   - Player selection

3. **Conversation**
   - Natural language input processing
   - Multi-turn conversations with context
   - Speech output via Home Assistant
   - Error handling with user feedback

4. **Agent Management**
   - Agent discovery via catalog
   - Dynamic agent selection
   - Agent switching without reload
   - Health monitoring

5. **Home Assistant Integration**
   - HACS installation support
   - Configuration flow
   - Options management
   - Automatic reload on changes

## 🔨 Technical Details

### API Endpoints

- `GET /agents` - Agent catalog discovery
- `POST /a2a/light-agent` - Light agent JSON-RPC endpoint
- `POST /a2a/music-agent` - Music agent JSON-RPC endpoint
- `GET /health` - Health check endpoint
- `GET /swagger` - API documentation

### Configuration Format

**Agent Connection String:**
```
Endpoint=https://localhost:7235;AccessKey=your-key;Model=gpt-4o;Provider=openai
```

**Supported Providers:**
- `openai` - OpenAI API
- `azureopenai` - Azure OpenAI Service
- `azureaiinference` - Azure AI Inference
- `ollama` - Ollama local models

### JSON-RPC Message Format

**Request:**
```json
{
  "jsonrpc": "2.0",
  "method": "message/send",
  "params": {
    "message": {
      "kind": "message",
      "role": "user",
      "parts": [{"kind": "text", "text": "Turn on the lights"}],
      "messageId": "uuid",
      "contextId": "uuid",
      "taskId": null
    }
  },
  "id": 1
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "result": {
    "kind": "message",
    "role": "assistant",
    "parts": [{"kind": "text", "text": "I've turned on the lights."}],
    "messageId": "uuid",
    "contextId": "uuid",
    "taskId": null
  },
  "id": 1
}
```

## 🐛 Bug Fixes

### Critical Fixes

1. **ConversationResponse Error** (Fixed)
   - **Issue**: Using non-existent `conversation.ConversationResponse` class
   - **Fix**: Replaced with proper `intent.IntentResponse` implementation
   - **Impact**: Home Assistant integration now works correctly

2. **TaskId Handling** (Fixed)
   - **Issue**: Agent Framework doesn't support task management
   - **Fix**: Set `taskId: null` in all JSON-RPC requests
   - **Impact**: Agents now respond successfully to messages

3. **Agent Selection Persistence** (Fixed)
   - **Issue**: Agent selection not persisting across restarts
   - **Fix**: Proper storage in config entry options
   - **Impact**: Selected agent maintained after reload

4. **SSL Certificate Issues** (Addressed)
   - **Issue**: Self-signed certificates causing connection failures
   - **Fix**: Added `verify=False` for development environments
   - **Impact**: Local development now works smoothly

## 📊 Performance

- **Agent Response Time**: < 2 seconds for typical requests
- **Light Discovery**: Cached with 30-minute refresh interval
- **Embedding Generation**: Optimized for real-time semantic search
- **Memory Usage**: Efficient entity caching
- **HTTP Communication**: Async throughout for non-blocking operations

## 📚 Documentation

### Updated Documentation

- **README.md**: Comprehensive project overview with pronunciation guide
- **AGENT_SELECTION.md**: Detailed agent selection feature documentation
- **Code Examples**: Complete agent and skill implementation examples
- **API Documentation**: Swagger/OpenAPI specifications
- **Installation Guide**: Both HACS and manual installation methods

### About the Name

Lucia is named after the ancient Nordic sun goddess associated with light, wisdom, and bringing illumination during the darkest time of year. Pronounced **LOO-sha** (or **LOO-thee-ah** in traditional Nordic), the name reflects the project's mission to bring intelligent automation and insight to your home.

## 🔮 What's Next

See our [Roadmap](https://github.com/seiggy/lucia-dotnet/blob/master/.docs/product/roadmap.md) for upcoming features:

### Phase 2: Core Agents (In Progress)
- ClimateAgent (HVAC and temperature)
- SecurityAgent (alarms, locks, cameras)
- SceneAgent (scene management)
- Multi-agent orchestration

### Phase 3: Intelligence (Planned)
- Pattern recognition and learning
- Automation suggestions
- Cost optimization for multi-LLM routing
- Local LLM refinements

## 🙏 Acknowledgments

Special thanks to:
- Microsoft Agent Framework team
- Home Assistant community
- Music Assistant project
- All contributors and testers

## 📦 Installation

### HACS (Recommended)

1. Open HACS in Home Assistant
2. Go to Integrations
3. Click three dots → Custom repositories
4. Add: `https://github.com/seiggy/lucia-dotnet`
5. Category: Integration
6. Find "Lucia" and download
7. Restart Home Assistant
8. Add integration via Settings → Devices & Services

### Manual Installation

```bash
# Clone repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet

# Install .NET 10
# Download from https://dotnet.microsoft.com/

# Run the agent host
dotnet run --project lucia.AgentHost

# Copy Home Assistant integration
cp -r custom_components/lucia /path/to/homeassistant/custom_components/

# Restart Home Assistant
```

## 🔗 Resources

- **Repository**: https://github.com/seiggy/lucia-dotnet
- **Issues**: https://github.com/seiggy/lucia-dotnet/issues
- **Discussions**: https://github.com/seiggy/lucia-dotnet/discussions
- **Documentation**: https://github.com/seiggy/lucia-dotnet/wiki

## 📄 License

MIT License - See [LICENSE](LICENSE) file for details.

---

**Built with ❤️ for the Home Assistant community**

*Bringing light to home automation, one agent at a time.* ☀️
