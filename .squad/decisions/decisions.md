# Lucia Orchestration & Caching Decisions Log






# Bishop — Shopping list fallback for todo-backed Home Assistant lists

## Context
Bug #42 showed that Lucia's shopping list endpoint assumed Home Assistant always exposed `GET /api/shopping_list`. That works for the native `shopping_list` integration, but CalDAV and other to-do providers expose list items through the todo platform instead.

## Decision
When `HomeAssistantClient.GetShoppingListItemsAsync()` receives `404 Not Found` from `/api/shopping_list`, Lucia should fall back to `todo.get_items` instead of surfacing the 404.

## Fallback selection
1. Prefer `todo.shopping_list` when present.
2. Otherwise prefer a todo entity whose entity id or `friendly_name` normalizes to `shopping list`.
3. If Home Assistant exposes exactly one todo entity, use that as the shopping list fallback.
4. If no reasonable todo-backed shopping list can be identified, rethrow the original 404.

## Rationale
This keeps native Home Assistant shopping list behavior unchanged while allowing CalDAV and other todo-backed shopping lists to load in Lucia without special user configuration. The heuristic stays conservative enough to avoid silently picking an arbitrary list when multiple unrelated todo entities exist.


---

# Idle CPU Investigation Report

**Author:** Brett (Voice/Speech Engineer)
**Date:** 2026-07-23
**Requested by:** Zack Way
**Status:** Investigation complete — no single smoking gun; several minor contributors

---

## Summary

No tight CPU spin was found. The idle CPU is most likely the **aggregate effect** of multiple 5-second database pollers, periodic background sweeps, and ONNX Runtime's native thread pool keeping cores warm for loaded models. The biggest individual contributors are the configuration providers that hit the database every 5 seconds unconditionally.

---

## Findings by Suspect (ranked by risk)

### 1. Configuration Pollers — ⚠️ MODERATE RISK

**SqliteConfigurationProvider** (`lucia.Data/Sqlite/SqliteConfigurationProvider.cs:21-127`)
- Polls every **5 seconds**
- Every tick: opens SQLite connection, runs `SELECT COUNT(*), COALESCE(MAX(updated_at), '') FROM configuration`
- On change: reloads all rows with `SELECT key, value FROM configuration`
- **Verdict:** Always hits DB every 5s even when nothing changes. The query is cheap but the connection open/close cycle adds up.
- **Fix suggestion:** Increase poll interval to 30s, or use SQLite WAL-mode file change notification instead of polling.

**MongoConfigurationProvider** (`lucia.Agents/Configuration/MongoConfigurationProvider.cs:21-99`)
- Polls every **5 seconds**
- Every tick: `AnyAsync()` on `UpdatedAt > _lastLoadTime`; on change, `ToListAsync()` entire collection
- **Verdict:** Hits Mongo every 5s. Has overlap guard but no change-detection shortcut.
- **Fix suggestion:** Increase poll interval to 30s, or switch to MongoDB change streams for push-based notification.

### 2. TaskArchivalService — ⚠️ MODERATE RISK

**TaskArchivalService** (`lucia.Agents/Services/TaskArchivalService.cs:44-113`)
- Polls every **5 minutes** (`TaskArchiveOptions.SweepInterval`)
- Every tick: enumerates **all tracked task IDs**, loads each task, checks terminal state, archives completed tasks
- **Verdict:** O(n) sweep unconditionally. If task count grows, this gets expensive. With InMemoryTaskStore, this is a full ConcurrentDictionary enumeration.
- **Fix suggestion:** Event-driven archiving (archive on task completion) or maintain a "pending archive" queue instead of full scan.

### 3. ONNX Runtime Resident Sessions — ⚡ LOW-MODERATE RISK

**GtcrnSpeechEnhancer** (`lucia.Wyoming/Audio/GtcrnSpeechEnhancer.cs:23-98`)
- Eagerly loads ONNX `InferenceSession` in constructor; kept alive as singleton for app lifetime
- No polling or background threads in application code

**SherpaSttEngine** (`lucia.Wyoming/Stt/SherpaSttEngine.cs:21-125`)
- Eagerly loads `OnlineRecognizer` in constructor; singleton lifetime

**HybridSttEngine** (`lucia.Wyoming/Stt/HybridSttEngine.cs:30-125`)
- Eagerly loads `OfflineRecognizer` in constructor; singleton lifetime

**GraniteOnnxEngine** (`lucia.Wyoming/Stt/GraniteOnnxEngine.cs:40-649`)
- Eagerly creates 1-3 `InferenceSession`s in `TryLoadModel`; kept alive until reload/dispose

**Verdict:** No app-level polling, but ONNX Runtime's native thread pool (intra-op/inter-op threads) stays alive and may show as baseline CPU. Multiple loaded sessions multiply this. The `ModelStartupValidator` runs warm-up inferences at startup only — not ongoing.

**Fix suggestion:** Consider lazy model loading (load on first voice session, unload after idle timeout). Or set `OrtEnv` thread pool sizes explicitly: `SessionOptions.IntraOpNumThreads = 1` for idle conservation.

### 4. InMemoryTaskStore Cleanup — 🟢 LOW RISK

**InMemoryTaskStore** (`lucia.Data/InMemory/InMemoryTaskStore.cs:23-112`)
- Timer fires every **5 minutes**
- Full `ConcurrentDictionary` scan to remove expired entries
- **Verdict:** O(n) but infrequent. Low risk unless task count is very large.

### 5. InMemoryPromptCacheService Cleanup — 🟢 LOW RISK

**InMemoryPromptCacheService** (`lucia.Data/InMemory/InMemoryPromptCacheService.cs:39-388`)
- Timer fires every **10 minutes**
- Scans both `_routingEntries` and `_chatEntries` for expired items
- **Verdict:** O(n) but very infrequent. Benign.

### 6. Wyoming Server Accept Loop — 🟢 BENIGN

**WyomingServer** (`lucia.Wyoming/Wyoming/WyomingServer.cs:71-119`)
- `AcceptTcpClientAsync(ct)` is properly awaited
- On exception: logs + **250ms delay** before retry
- **Verdict:** Not a tight spin. Would retry at ~4/sec on persistent failure, but that's bounded.

### 7. SSE Streaming Endpoints — 🟢 BENIGN

**BackgroundTaskApi** (`lucia.AgentHost/Apis/BackgroundTaskApi.cs:34-73`)
- Event-driven via `TaskCompletionSource` with 2s heartbeat timeout
- Client disconnect cancels the loop cleanly
- **Verdict:** No spin risk.

**EntityLocationCacheApi** (`lucia.AgentHost/Apis/EntityLocationCacheApi.cs:75-105`)
- Polls every **750ms**, serializes embedding progress
- **Verdict:** Minor CPU per connected client, but benign. Only a concern with many simultaneous dashboard clients.

### 8. Entity Location Service — 🟢 BENIGN

**EntityLocationService** (`lucia.Agents/Services/EntityLocationService.cs:121-1110`)
- No background timer loop; refresh is **on-demand** via `EnsureFreshAsync()`
- Freshness throttled to 30-second intervals
- Reloads are heavy (HA registry calls, embedding generation) but only triggered by requests
- **Verdict:** Not an idle CPU source.

### 9. Redis/MongoDB Keep-Alive — 🟢 BENIGN

- Redis: No custom `ConfigurationOptions` or heartbeat settings found. Uses Aspire defaults.
- MongoDB: No custom `MongoClientSettings` or heartbeat frequency. Default driver heartbeat is 10s — negligible.
- **Verdict:** Not a CPU source.

### 10. Background Services (24h cycles) — 🟢 BENIGN

**TraceRetentionService** (`lucia.Agents/Training/TraceRetentionService.cs:16-56`) — runs once per 24h
**ProvisionalProfileCleanupService** (`lucia.Wyoming/Diarization/ProvisionalProfileCleanupService.cs:30-67`) — runs once per 24h
- **Verdict:** Far too infrequent to matter.

---

## Recommended Actions (prioritized)

1. **Increase config poll intervals** from 5s → 30s for both SQLite and Mongo providers. Hot-reload latency of 30s is acceptable for config changes.
2. **Set ONNX thread pool sizes** explicitly (`IntraOpNumThreads = 2`, `InterOpNumThreads = 1`) to limit native threads from loaded models.
3. **Consider lazy model loading** — load voice models on first session, unload after N minutes idle.
4. **Make TaskArchivalService event-driven** — archive tasks on completion instead of periodic full scan.
5. **Profile with `dotnet-counters`** to confirm which of these is the actual top contributor before making changes.


---

### 2026-05-29T11:05:10-04:00: User directive
**By:** Zack Way (via Copilot)
**What:** All agents responsible for writing code should prefer Claude Opus 4.8 (`claude-opus-4.8`) as their model. Non-code agents (Scribe, Ralph) remain on their defaults.
**Why:** User request — captured for team memory


---

# Decision: Jetson Nano ARM64 Dockerfile & Compose

**Date:** 2026-03-29  
**Author:** Hicks (DevOps / Infrastructure Engineer)  
**Scope:** Docker images and deployment configuration  
**Status:** ACCEPTED

## Problem Statement

Lucia needs to support deployment on NVIDIA Jetson Nano (ARM64/aarch64) edge devices. The existing Dockerfiles (standard x64, GPU voice variant, CPU voice variant) do not support ARM64. Additionally, the speech pipeline (Wyoming + ONNX Runtime) has dependency gaps on ARM64 (no .NET ONNX Runtime support for GPU or CPU inference on ARM), requiring a new build variant that excludes the speech pipeline entirely.

## Decision

Create two new infrastructure files to enable ARM64 Jetson Nano deployment:

1. **`infra/docker/Dockerfile.agenthost-jetson`** — Multi-stage Dockerfile for ARM64 without speech pipeline
2. **`infra/docker/docker-compose.jetson.yml`** — Docker Compose configuration with resource constraints optimized for 4GB RAM Jetson boards

### Key Design Choices

#### 1. ExcludeSpeech Precompiler Flag
- Pass `-p:ExcludeSpeech=true` at **restore**, **build**, and **publish** stages to ensure speech-related dependencies are never included.
- Matches Parker's .NET code changes that conditionally compile speech features.
- No runtime env vars needed — pure compile-time gate.

#### 2. ARM64 Base Images
- **Runtime:** `mcr.microsoft.com/dotnet/aspnet:10.0-noble-arm64v8` (minimal Ubuntu Noble, ARM64-optimized)
- **Build:** `mcr.microsoft.com/dotnet/sdk:10.0-noble-arm64v8`
- Both images are official Microsoft images, pinned to .NET 10.0, and widely available in public registries.

#### 3. No Assets Stage
- Removed the assets/pre-built binaries pattern — no Wyoming models, no GPU ONNX Runtime libs needed.
- Simplified build: only `node-build` → `base` → `build` → `publish` → `final` (5 stages instead of 6).

#### 4. Separate Compose File
- `docker-compose.jetson.yml` is independent of `docker-compose.yml` (standard x64 deployment).
- Allows different resource limits without conditional logic in a single file.
- Tighter constraints: Redis 128MB (vs 256MB), MongoDB 256MB (vs 512MB), AgentHost 512MB (vs 1GB).

#### 5. Security & Non-Root User
- Same non-root user pattern (appuser, UID 1100) as standard images.
- Same health check, read-only filesystem, and capability dropping.
- No voice models → no `/app/models` volume (only `/app/plugins` for future extensibility).

## Rationale

### Why Separate Jetson Compose?
A single `docker-compose.yml` with profiles would require conditional resource limits, making the file harder to read and maintain. Separate compose files follow the pattern of existing variants (e.g., `docker-compose.lucia-sidecar.yml`, `docker-compose.voice.yml`).

### Why No GPU Support on Jetson?
Jetson's NVIDIA CUDA runtime is available, but `.NET ONNX Runtime` lacks ARM64 GPU binaries. The Wyoming speech pipeline depends on ONNX Runtime. Fixing this would require:
1. Compiling ONNX Runtime from source for ARM64 CUDA (weeks of work, many edge cases)
2. Or using a fallback SLM (small language model) for speech on Jetson

This decision chooses pragmatism: deploy Jetson **without speech** for now, unblocking the use case. Speech can be added later when `.NET ONNX Runtime` officially supports ARM64 or a fallback SLM is integrated.

### Why ExcludeSpeech in Build?
Compile-time gates prevent accidental inclusion of speech dependencies even if Wyoming package is restored. Runtime env vars are easier to forget; build-time gates are bulletproof.

## Files Created

- `infra/docker/Dockerfile.agenthost-jetson` (219 lines) — ARM64 build without speech
- `infra/docker/docker-compose.jetson.yml` (280 lines) — Jetson deployment config

## Files Modified

- `infra/docker/README.md` — Added "Jetson Nano ARM64 Deployment" section with build/deploy examples and comparison table

## Testing & Validation

Before merging:

1. **Build locally** (on ARM64 board or cross-compile):
   ```bash
   docker build -f infra/docker/Dockerfile.agenthost-jetson -t lucia:jetson .
   ```

2. **Start compose**:
   ```bash
   docker compose -f docker-compose.jetson.yml up -d
   ```

3. **Verify health**:
   ```bash
   docker compose -f docker-compose.jetson.yml ps
   curl http://localhost:7233/health
   ```

4. **Confirm speech is disabled** — Check app logs; verify no Wyoming/ONNX warnings.

## Impact

- ✅ **New capability:** Jetson Nano (and all ARM64 boards) now supported
- ✅ **No breaking changes** — Standard x64 images and compose unchanged
- ✅ **Consistent patterns** — Follows existing Dockerfile and compose conventions
- ❌ **No speech pipeline on Jetson** — Acceptable trade-off; local LLM + Home Assistant automation still works without voice

## Future Work

1. **Speech on Jetson** — Once `.NET ONNX Runtime` ARM64 support lands, re-enable speech by removing `ExcludeSpeech=true`.
2. **Jetson-specific SLM** — Explore lightweight speech models (e.g., SherpaONNX CPU) that don't require ONNX Runtime GPU bindings.
3. **Kubernetes Jetson** — Add Helm chart variant for multi-Jetson clusters.

---

**Approved by:** Zack Way  
**Co-author:** Hicks (GitHub Copilot CLI)


---

# Kane — MCP tool picker status behavior

## Context
The agent definition editor was showing `Server not connected` for configured MCP servers even when the user expected tool selection to be available.

## Decision
In the dashboard editor, treat MCP tool availability as an active load step instead of a passive status read:
- fetch configured MCP servers
- read current runtime statuses
- attempt to connect enabled servers that are not already connected
- discover tools after connection
- if discovery still fails, show an explicit `MCP tools unavailable: …` message

## Why
`/api/mcp-servers/status` reports the runtime registry state, not a persisted capability snapshot, so relying on it alone makes the editor look broken and misleading.

## Follow-up
A backend endpoint that returns persisted server metadata plus tool availability would let the dashboard avoid on-load connect attempts in the future.


---

# Parker — Agent Timeout Handling for Bug #106

**Date:** 2026-05-25  
**Status:** Proposed  
**Related Issue:** #106

## Summary

The current codebase does **not** contain a 9-10 second hardcoded agent execution timeout for orchestration. The default agent-invoker timeout is 30 seconds, and Home Assistant HTTP calls default to 60 seconds. The most likely failure mode for the reported music-agent trace is an upstream request cancellation (voice pipeline timeout, HTTP disconnect, or another caller-owned cancellation token) propagating into the orchestration workflow.

## Decision Proposal

When an agent has already produced a structured failure result, orchestration should continue aggregating and reporting that result even if the caller's cancellation token has been tripped. Internal workflow event recording should not be allowed to turn a specific agent cancellation into Lucia's generic fallback message.

## Applied Change

1. Local and remote agent invokers now translate `OperationCanceledException` into descriptive user-facing failures instead of surfacing `"A task was canceled."`.
2. `ResultAggregatorExecutor` now records workflow events with `CancellationToken.None` so late request cancellation does not prevent graceful aggregation after agent results already exist.
3. Added regression coverage for:
   - default 30-second agent timeout,
   - graceful timeout/cancellation messaging in `LocalAgentInvoker`,
   - aggregation surviving a canceled pipeline token while still returning the agent-specific failure.

## Rationale

This keeps platform behavior aligned with user expectations: if the underlying problem is an upstream cancellation, Lucia should still report a meaningful agent-level interruption instead of collapsing into a generic orchestration error. It also gives the team cleaner telemetry for separating true agent timeouts from request-lifetime cancellations.


---

## Context

Sensor, light, climate, and other skills rely on cached embeddings for hybrid entity matching. When the configured embedding provider changes, previously persisted vectors may be stale or dimensionally incompatible with the new provider.

## Decision

On embedding-provider changes, skills that cache embeddings should force a one-time rebuild from source data instead of trusting cached embeddings. For the sensor agent fix, `SensorControlSkill` now bypasses Redis cache reads during invalidation, regenerates embeddings from Home Assistant state, and then repopulates cache entries with the new provider output.

## Rationale

Persisted embeddings are provider-specific artifacts, not stable business data. Rebuilding on provider change avoids mismatched vector spaces and keeps Redis useful for steady-state reads after the refresh completes.


---

# Parker — EXCLUDE_SPEECH flag for ARM / Jetson builds

## Context

Jetson Nano ARM64 builds need to run `lucia.AgentHost` without the Wyoming speech runtime because the current .NET voice pipeline depends on Sherpa-ONNX and ONNX Runtime packages that are difficult to ship for this target.

## Decision

Add an `ExcludeSpeech` MSBuild property that defines the `EXCLUDE_SPEECH` compilation symbol through `Directory.Build.props`.

When `ExcludeSpeech=true`:

1. `lucia.Wyoming` keeps command-routing types and registrations needed by `/api/conversation`.
2. `lucia.Wyoming` conditionally skips ONNX / Sherpa / audio package references.
3. `lucia.Wyoming` conditionally removes ONNX-backed engine/session/server source files from compilation.
4. `lucia.AgentHost` conditionally removes speech-only API files and skips mapping the Wyoming / voice endpoints.

## Why

Removing the full `lucia.Wyoming` project reference would also remove command-routing primitives used by the conversation fast-path. The correct boundary is to preserve text command routing but exclude the speech runtime, model-management surface, and Wyoming server pieces.

## Validation

- `dotnet build .\lucia.AgentHost\lucia.AgentHost.csproj`
- `dotnet build .\lucia.AgentHost\lucia.AgentHost.csproj -p:ExcludeSpeech=true`

Both builds succeeded locally after the change.


---

# Decision: PR cleanup branches must drop generated and binary artifacts

- **Date:** 2026-05-18
- **Owner:** Parker
- **Context:** Cleaning PR #116 for merge onto current `master`

## Decision
When rescuing older PR branches, preserve the current `master` repository shape and remove accidental artifacts introduced by the stale branch. For PR #116 this meant removing tracked `.onnx` model binaries, deleting the backup `vite.config.ts.bak`, dropping the malformed `lucia-dashboard/obj` path entry, and keeping the existing `lucia.EvalHarness.Reports.HtmlReportGenerator` implementation instead of introducing a second `HtmlReportGenerator` type in `lucia.EvalHarness.Tui`.

## Why
These files are not source-of-truth application code and they either bloat the repository, represent generated output, or create avoidable merge/build failures on top of current `master`.

## Follow-up
- Keep `*.onnx` ignored in the repo.
- Prefer preserving `master` versions of tracked project metadata and generated-output exclusions during PR rescue work.


---

# CPU Idle Optimization — Implementation Plan

**Author:** Ripley (Lead / Eval Architect)
**Date:** 2025-07-23
**Status:** Approved — ready for implementation
**Based on:** Brett's CPU Investigation (`brett-cpu-investigation.md`)

---

## Executive Summary

Brett's investigation correctly identified the two highest-impact contributors to idle CPU:
1. **Config pollers** hitting DB every 5 seconds unconditionally (SQLite + Mongo)
2. **ONNX Runtime native thread pools** staying warm across multiple singleton sessions

Both findings are validated. This plan assigns concrete fix tasks to Parker (config) and Brett (ORT).

---

## Issue 1: Config Pollers — 5-Second Unconditional DB Polling

### Validated Root Cause

Both `SqliteConfigurationProvider` and `MongoConfigurationProvider` use `System.Threading.Timer` at a 5-second interval. Every tick:
- **SQLite:** Opens a new connection, runs `SELECT COUNT(*), COALESCE(MAX(updated_at), '') FROM configuration`, computes a hash, and only reloads all rows if the hash changed. The change-detection is present but the connection open/close cycle runs unconditionally every 5s.
- **Mongo:** Runs `AnyAsync()` with a `Gt(UpdatedAt, _lastLoadTime)` filter against MongoDB every 5s.

Key insight: `ConfigurationApi.UpdateSectionAsync` already calls `configRoot.Reload()` after writes (line 225 of `ConfigurationApi.cs`). This means the 5s poll is **only needed for external changes** (direct DB edits, multi-instance deployments). That's a rare event — the poll can be very slow.

### Chosen Fix: Increase Default to 30s + Make Configurable

**Approach:** Change the default `PollInterval` from `TimeSpan.FromSeconds(5)` to `TimeSpan.FromSeconds(30)` in both Source classes. Keep the `pollInterval` constructor parameter so it remains overridable.

**Why 30s:** Config changes from direct DB edits are rare. A 30-second lag is imperceptible for human-initiated config changes. The `configRoot.Reload()` on API writes already provides instant reload for normal operations.

**Why not longer (60s+):** Some users may expect reasonably fast feedback from the admin UI after a DB edit. 30s is a pragmatic middle ground.

### Rejected Alternatives

| Alternative | Why Rejected |
|---|---|
| **SQLite WAL file-change notification** | Requires filesystem inotify, fragile across NFS/Docker volumes, and the current hash-based change detection in SQLite is already efficient — the problem is just the interval |
| **MongoDB change streams** | Requires a replica set (standalone MongoDB won't work). This project supports both standalone and replica set deployments. Too restrictive. |
| **Remove polling entirely** | Would break detection of external changes (multi-instance, direct DB edits) |
| **Event-driven with SignalR/Redis pub-sub** | Massive over-engineering for a config poller. Adds infrastructure dependency. |

### Additional Fix: Mongo Provider Race Condition

The `MongoConfigurationProvider` uses `volatile bool _isPolling` instead of `Interlocked.CompareExchange`. This is a data race — two timer callbacks could both read `_isPolling == false` before either sets it to `true`. The SQLite provider correctly uses `Interlocked`. Fix the Mongo provider to match.

---

## Issue 2: ONNX Runtime Native Thread Pool — Warm Sessions

### Validated Root Cause

Multiple singleton ONNX sessions are loaded eagerly at startup and kept alive for the app lifetime:

| Engine | Sessions | IntraOp Threads | InterOp Threads |
|---|---|---|---|
| `GtcrnSpeechEnhancer` | 1 `InferenceSession` | 2 | 1 |
| `GraniteOnnxEngine` | 1–3 `InferenceSession` | `_options.NumThreads` (default: 4) | 1 |
| `SherpaSttEngine` | 1 `OnlineRecognizer` (wraps ORT) | via `NumThreads` (default: 4) | N/A (sherpa manages) |
| `HybridSttEngine` | 1 `OfflineRecognizer` (wraps ORT) | via `NumThreads` (default: 4) | N/A (sherpa manages) |
| `SherpaDiarizationEngine` | 1 `SpeakerEmbeddingExtractor` | 2 | N/A (sherpa manages) |

**Worst case:** If all engines are enabled, that's 7+ ORT sessions × 2–4 intra-op threads each = 14–28 native threads with active spin loops. ORT's default spin control keeps these threads spinning for work even when idle, which shows as baseline CPU on `top`.

The `ModelStartupValidator` runs warm-up inferences at startup only — not ongoing. The CPU drain is from the thread pool spin, not from inference.

### Chosen Fix: Two-Phase Approach

#### Phase 1 (Quick Win): Environment Variable `ORT_THREADPOOL_SPIN_CONTROL=0`

Set `ORT_THREADPOOL_SPIN_CONTROL=0` in the container entrypoint / docker-compose / Aspire AppHost. This tells ORT's thread pool to sleep when idle instead of busy-spinning. It is the single highest-impact change with zero code modifications.

**Tradeoff:** First inference after idle will have ~1-5ms extra latency as threads wake from sleep instead of being already spinning. For voice processing at 16kHz, this is imperceptible — the audio buffer alone is hundreds of milliseconds.

#### Phase 2 (Code Change): Reduce Default `NumThreads` from 4 to 2

Change the default `NumThreads` in all options classes from `4` to `2`:
- `SttModelOptions.NumThreads` → 2
- `SttOptions.NumThreads` → 2
- `HybridSttOptions.NumThreads` → 2
- `GraniteOptions.NumThreads` → 2
- `OfflineSttOptions.NumThreads` → 2

**Rationale:** These engines process short audio clips (1–30 seconds). On a typical 4–8 core machine, 4 intra-op threads per session is excessive, especially with multiple engines loaded. 2 threads per session still enables parallelism for ORT graph execution while cutting native thread count in half.

The GTCRN enhancer and diarization engine already use `NumThreads = 2` — they're correctly sized.

### Rejected Alternatives

| Alternative | Why Rejected |
|---|---|
| **Lazy-load models on first voice session** | Adds 2–8 seconds of cold-start latency on first voice command. Unacceptable for a voice assistant. The `ModelStartupValidator` explicitly warm-starts for this reason. |
| **Idle timeout + dispose after N minutes** | Same cold-start problem. Model reload is expensive (disk I/O, memory allocation, graph optimization). Users expect instant response when they say the wake word. |
| **`IntraOpNumThreads = 1` for idle sessions** | ORT `SessionOptions` are immutable after session creation. Would require disposing and recreating sessions, which is equivalent to lazy loading — same latency problem. |
| **Use ORT's `DisablePerSessionThreads`** | Shares a global thread pool across sessions — reduces total threads but removes ability to control per-engine parallelism. Could cause contention between engines. |

---

## Task Breakdown

### Parker (Backend / Config) — Config Poller Fixes

**Task P1: Increase default poll interval to 30s**

Files to change:
- `lucia.Data/Sqlite/SqliteConfigurationSource.cs` line 20: `TimeSpan.FromSeconds(5)` → `TimeSpan.FromSeconds(30)`
- `lucia.Agents/Configuration/MongoConfigurationSource.cs` line 25: `TimeSpan.FromSeconds(5)` → `TimeSpan.FromSeconds(30)`
- `lucia.Data/Sqlite/SqliteConfigurationProvider.cs` line 28: `TimeSpan.FromSeconds(5)` → `TimeSpan.FromSeconds(30)`
- `lucia.Agents/Configuration/MongoConfigurationProvider.cs` line 25: `TimeSpan.FromSeconds(5)` → `TimeSpan.FromSeconds(30)`

Acceptance criteria:
- Both providers default to 30s polling
- Existing `pollInterval` constructor parameter still works for custom intervals
- `configRoot.Reload()` in `ConfigurationApi.UpdateSectionAsync` still provides instant reload on API writes
- No behavior change visible to users (config changes via admin UI are still instant)

**Task P2: Fix Mongo provider race condition**

File: `lucia.Agents/Configuration/MongoConfigurationProvider.cs`
- Change `private volatile bool _isPolling;` → `private int _isPolling;`
- Change poll guard from `if (_isPolling) return; _isPolling = true;` → `if (Interlocked.CompareExchange(ref _isPolling, 1, 0) != 0) return;`
- Change cleanup from `_isPolling = false;` → `Interlocked.Exchange(ref _isPolling, 0);`

This matches the pattern already used correctly in `SqliteConfigurationProvider`.

Acceptance criteria:
- No overlapping polls possible even under timer re-entrance
- Tests still pass

---

### Brett (Voice / ORT) — ONNX Thread Pool Fixes

**Task B1: Set `ORT_THREADPOOL_SPIN_CONTROL=0` in deployment configs**

Files to change:
- `infra/docker/docker-compose.yml` — add to voice service environment
- `infra/docker/docker-compose.voice.yml` — add to voice service environment
- Any Aspire AppHost `Program.cs` that configures the voice project — add `.WithEnvironment("ORT_THREADPOOL_SPIN_CONTROL", "0")`
- `infra/docker/Dockerfile.voice` and `Dockerfile.voice-cpu` — add `ENV ORT_THREADPOOL_SPIN_CONTROL=0`

Acceptance criteria:
- Voice container starts with spin control disabled
- `dotnet-counters` or `top` shows measurably lower idle CPU (expect 5–15% reduction on a 4-core system)
- Voice latency regression < 5ms on first inference after idle (measure with existing model warm-up in `ModelStartupValidator`)

**Task B2: Reduce default `NumThreads` from 4 to 2**

Files to change:
- `lucia.Wyoming/Models/SttModelOptions.cs` line 9: `NumThreads = 4` → `NumThreads = 2`
- `lucia.Wyoming/Stt/SttOptions.cs` line 9: `NumThreads = 4` → `NumThreads = 2`
- `lucia.Wyoming/Stt/HybridSttOptions.cs` line 35: `NumThreads = 4` → `NumThreads = 2`
- `lucia.Wyoming/Stt/GraniteOptions.cs` line 14: `NumThreads = 4` → `NumThreads = 2`
- `lucia.Wyoming/Stt/OfflineSttOptions.cs` line 15: `NumThreads = 4` → `NumThreads = 2`

Acceptance criteria:
- All STT engines default to 2 intra-op threads
- Config override still works (`Wyoming:Stt:NumThreads` in appsettings)
- Transcription accuracy unchanged (run a sample voice clip, verify output matches)
- Transcription latency increase < 20% on typical 5-second audio clips (parallelism still helps, just with fewer threads)

---

## Risks and Gotchas

1. **Config poll interval + multi-instance:** If running multiple lucia instances against the same DB, a config change on instance A takes up to 30s to propagate to instance B. Document this in the config README. The admin UI on the instance that made the change gets instant reload via `configRoot.Reload()`.

2. **`ORT_THREADPOOL_SPIN_CONTROL` availability:** This env var was introduced in ORT 1.14+. Our ORT version should be recent enough, but Brett should verify by checking the NuGet package version of `Microsoft.ML.OnnxRuntime` in `lucia.Wyoming.csproj`.

3. **Sherpa-onnx thread control:** Sherpa's `NumThreads` maps to the underlying ORT `IntraOpNumThreads`. The `ORT_THREADPOOL_SPIN_CONTROL=0` env var should also affect sherpa-onnx sessions since they use the same ORT runtime. Verify experimentally.

4. **NumThreads = 2 on high-core-count machines:** Users with 16+ cores who want max transcription speed can override via config. The default of 2 is conservative and correct for the common deployment (Raspberry Pi 5 / mini PC with 4–8 cores).

5. **Timer disposal:** Both config providers have `Dispose()` methods that null out the timer. Verify these are called during graceful shutdown (the `IDisposable` on the provider should be handled by the configuration system, but worth a quick check).

---

## Execution Order

1. **B1 first** — env var change, zero risk, immediate impact
2. **P1 + P2 together** — simple config changes, low risk
3. **B2 last** — needs latency benchmarking to validate

Brett and Parker can work independently. No cross-dependencies between their tasks.


---

# Decision — Solution Health Review Synthesis (Ripley)

**Date:** 2026-05-29
**Author:** Ripley (Lead / Eval Architect)
**Requested by:** Zack Way
**Artifact:** `HEALTH-REPORT.md` (session files) — consolidated from 9 specialist reviews

## Decision

Adopt a **three-wave remediation sequence** for the lucia-dotnet whole-solution health review. No Critical defects; 20 High and ~19 notable Medium findings across 9 domains.

### Wave 1 — Restore the safety net (blocking, do first)
1. Fix squad CI/CD: repoint triggers to `master`, replace `echo` stub build/test/release steps with real `dotnet` commands, fix version source (drop non-existent root `package.json`).
2. Orchestration observability: `using var activity` on root span; align ActivitySource/meter name casing (`Lucia.*` → `lucia.*`).
3. UTC fix in all agent config-refresh gates (kills per-request rebuilds across 8 agents).
4. Close unauthenticated Scalar/OpenAPI surface in both hosts.

**Rationale:** Without a working CI gate, no later fix can be regression-protected. These also directly restore three stated project principles (observability, privacy/security, build protection) and are mostly S-effort.

### Wave 2 — Make routing verifiable & evals trustworthy
- Deterministic CI tests for `RouterExecutor`/`ResultAggregatorExecutor`/`RemoteAgentInvoker`/`LuciaEngine` using existing stubs.
- Eval determinism (typed seed, fixed default seed), lifetime (dispose chat clients), timeouts (per-call CTS), and stop synthesizing constant judge/benchmark scores.

### Wave 3 — Harden latent hazards
- Data: SQLite UTC timestamp normalization, per-request auth write throttling, `pg_trgm` indexes.
- HA/Voice: WebSocket internal timeouts, captive HttpClient fix, unbounded utterance buffer cap, STT semaphore scoping, CT propagation.
- Infra: SHA-pin actions, asset checksums, dev/prod Mongo parity.

## Cross-cutting themes flagged for team awareness
1. CI/build gates are decorative, not protective (Infra + Tests + Voice).
2. Observability fails where it matters (Architecture; Voice is the counter-example to emulate).
3. `DateTime.Now` vs `UtcNow` systemic timestamp hazard (Architecture + Hosts + Data + Tests).
4. Inconsistent cancellation/timeout discipline (Hosts + HA + Voice + Eval).
5. HttpClient/resource lifetime leaks (HA + Eval + Voice + Dashboard).
6. False confidence via swallowed errors / synthesized scores (Tests + Eval + Infra + Dashboard).

## Next action
Offered to open GitHub issues for all High findings (#1-#20) grouped as three milestone waves, and to dispatch fix branches for S-effort quick wins. Recommend a single blocking "CI gate" issue (#1-#3) first.

---

# Inbox Re-triage Decision

**Author:** Ripley (Lead / Eval Architect)  
**Date:** 2026-05-30  
**Status:** Complete  
**Decision Type:** Issue Triage

## Problem

All 50 open issues carrying the `squad` label were also labeled `squad:lambert` after a bulk-triage operation. Lambert's domain is narrow: writing test scenarios, adding real assertions to eval suites, skill unit tests, and provider-free test coverage. 46 of the 50 issues were misrouted.

## Routing Rationale

Each issue was routed by matching its title keywords to the team domain map.

### squad:brett — 6 issues
`#183 #182 #180 #179 #178 #177`

Wyoming protocol, mDNS/InfoEvent, STT, GtcrnStreamingSession, speech buffers, pipeline-stage timings are all explicitly Brett's domain. Every issue in this group names one or more of those artifacts directly.

### squad:parker — 17 issues
`#176 #175 #174 #173 #172 #171 #170 #169 #168 #167 #166 #165 #158 #154 #153 #145 #140`

Parker owns .NET API hosts, orchestration spans/meters, auth/tokens, async cancellation, HttpClient lifetime, OpenAPI gating, ChatOptions, num_ctx, and SSE. This is the largest bucket because the bulk of the open work is backend-platform hardening: API validation, token security, OTel naming, cancellation deadlines, AppHost alignment, and SSE streaming.

### squad:hicks — 11 issues
`#181 #164 #162 #161 #159 #155 #151 #147 #142 #138 #135`

Hicks owns Docker images/digests, compose, GitHub Actions pinning, Trivy scanning, CI gates, infra lint, and asset image tags. All 11 issues are infrastructure hardening: Docker digest pinning, asset SHA tags, Actions pinning, Trivy coverage, infra lint gates, CI branch alignment, and CI build stubs.

### squad:lambert — 4 issues (kept as-is)
`#144 #148 #152 #156`

These four genuinely belong to Lambert:
- #144 — Add deterministic CI tests for the orchestration routing brain (test scenario writing)
- #148 — Make eval suites opt-in and back behaviors with provider-free tests (provider-free coverage)
- #152 — Add real assertions to no-assert speech benchmark tests (adding assertions)
- #156 — Add unit tests for SceneControlSkill and ListSkill (skill unit tests)

### squad:dallas — 4 issues
`#134 #137 #141 #150`

Dallas owns core eval engine code, scoring/aggregation logic, sweep/optimizer mechanics, and judge-score computation. All four issues are engine-level: N-sweep winner selection, synthesized constant judge scores, per-dimension scoring, and eval profile parameter override determinism.

### squad:kane — 3 issues
`#136 #139 #143`

Kane owns the React dashboard, UI, error boundaries, and typed API responses. These three are pure frontend: global error boundary, unsafe `as`/`any` API response casts, and error UI for template/optimizer fetches.

### squad:bishop — 3 issues
`#149 #157 #160`

Bishop owns the HA custom component, services.yaml, and HA WebSocket/token validation. Three issues map exactly: HA access token validation before WS open, services.yaml for lucia.send_message, and moving dev/debug scripts out of the shippable component root.

### squad:ripley — 1 issue
`#146`

Centralize and document eval pass thresholds — pass-threshold policy is explicitly Ripley's remit as Lead / Eval Architect.

### squad:ash — 1 issue
`#163`

Use UTC consistently for all persisted and compared timestamps — timestamp persistence is the data engineering domain (Ash owns trace/transcript persistence and datasets).

## Summary Table

| Member  | Count | Issues                                                                 |
|---------|-------|------------------------------------------------------------------------|
| parker  | 17    | #176 #175 #174 #173 #172 #171 #170 #169 #168 #167 #166 #165 #158 #154 #153 #145 #140 |
| hicks   | 11    | #181 #164 #162 #161 #159 #155 #151 #147 #142 #138 #135                |
| brett   | 6     | #183 #182 #180 #179 #178 #177                                          |
| lambert | 4     | #144 #148 #152 #156 (kept)                                             |
| dallas  | 4     | #134 #137 #141 #150                                                    |
| kane    | 3     | #136 #139 #143                                                         |
| bishop  | 3     | #149 #157 #160                                                         |
| ripley  | 1     | #146                                                                   |
| ash     | 1     | #163                                                                   |
| **Total** | **50** |                                                                      |
