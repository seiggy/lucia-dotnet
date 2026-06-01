# Lucia Orchestration & Caching Decisions Log

# Decision: InputRequired Task Auto-Cancel Timeout

**Date:** 2026-06-01  
**Author:** Parker (Backend / Platform Engineer)  
**Requested by:** Zack Way  

---

## Context

Tasks in the lucia multi-agent orchestration that enter `TaskState.InputRequired` (set in `LuciaEngine.cs:188` when `workflowResult.NeedsInput == true`) were getting stuck indefinitely. The only safety net was the 24-hour Redis TTL on `RedisTaskStore`. No code-level timeout existed; a task awaiting human input would sit in that state until Redis expired it, causing the agent session to appear permanently hung.

---

## Decision

Implement a **background sweeper service** (`InputRequiredTimeoutService : BackgroundService`) that periodically scans all known task IDs, identifies tasks in `InputRequired` that have exceeded a configurable timeout, and transitions them to `TaskState.Canceled`.

**Why a sweeper, not a per-task `CancellationTokenSource`:**  
Tasks are durable — persisted in Redis and surviving process restarts. A per-task CTS lives in memory only; it would be silently lost on restart, providing no protection after a pod recycle. The sweeper re-derives timeout state from the stored `TaskStatus.Timestamp` on every startup, making it restart-safe.

---

## Configuration

Options class: `InputRequiredTimeoutOptions` (section key: `InputRequiredTimeout`)

| Key | Default | Notes |
|-----|---------|-------|
| `Timeout` | `00:01:00` (1 min) | How long a task may stay in InputRequired before auto-cancel |
| `SweepInterval` | `00:00:10` (10 s) | How often the sweeper checks |

Override in appsettings or environment:
```json
"InputRequiredTimeout": {
  "Timeout": "00:05:00"
}
```

---

## Why 1 Minute (Zack's Voice-Engine Reasoning — 2026-06-01)

Lucia is a **voice response engine**. After the LLM delivers a final response and creates a task awaiting input, a human voice reply typically arrives within 6–10 seconds. Even a long speech-to-text utterance rarely exceeds 15–20 seconds. 30 seconds was too tight (a slow speaker or noisy environment could lose the race), and 5 minutes is far too lenient for a real-time voice loop. **1 minute** was chosen as a safe buffer that won't cut off any realistic voice interaction while still keeping agent sessions from hanging for long periods.

Deployments using a chat UI or other asynchronous input channels should increase this value to suit their expected response cadence.

---

## Idempotency / Concurrency

- **Double-check re-read:** Before writing `Canceled`, the sweeper re-reads the task from the store. If input arrived concurrently (between first and second read), the fresh state will differ and the cancel is skipped.
- **Late input after cancel:** If input arrives after the task is already `Canceled`, the task's state is no longer `InputRequired`; any handler checking state will see `Canceled` and respond appropriately (not double-complete).
- **Repeated sweeps:** Canceled tasks have `State != InputRequired` and are skipped in every subsequent sweep pass. Idempotent by design.

---

## Key Files

| File | Role |
|------|------|
| `lucia.Agents/Configuration/InputRequiredTimeoutOptions.cs` | Options class, defaults, section name |
| `lucia.Agents/Services/InputRequiredTimeoutService.cs` | Sweeper implementation; `SweepAsync` is `internal` for test access |
| `lucia.Agents/Services/InputRequiredTimeoutLogMessages.cs` | Compile-time `[LoggerMessage]` attributes |
| `lucia.Agents/Extensions/ServiceCollectionExtensions.cs` | DI registration (`Configure<>` + `AddHostedService<>`) |
| `lucia.Tests/Services/InputRequiredTimeoutServiceTests.cs` | 12 unit tests; `FakeTimeProvider` + `InMemoryTaskStore`; no real sleeps |

---

## Follow-Up

- Consider surfacing `InputRequired` timeout as a per-context/per-agent override (not just global) if different agent types have different human-response expectations.
- The `SessionManager.UpdateTaskStatusAsync` hardcodes `DateTimeOffset.UtcNow` — consider threading `TimeProvider` through it for complete testability of the task lifecycle.


# Decision: Jetson Non-Voice Deploy — Final Outcome

**Date:** 2026-05-31  
**Author:** Hicks (DevOps)  
**Status:** ✅ DEPLOYED & VALIDATED

---

## Context

Retry of the Jetson non-voice platform deploy after previous run hit a subnet routing block (192.168.0.x host could not reach 192.168.1.x Jetson). Zack bounced the Jetson; it came back online on 192.168.1.239.

---

## Deploy Outcome

### Connectivity
SSH to `zackw@192.168.1.239` ✅ connected with key-based auth. No password prompt, no host-key interaction needed. L3 ping 3–23ms RTT.

### Jetson State Before Deploy
| Item | State |
|------|-------|
| Compose project | `zackw` from `/home/zackw/docker-compose.jetson.yml` |
| lucia-jetson image | `seiggy/lucia-agenthost:jetson` (registry, not on-device build) |
| lucia-jetson health | **unhealthy** (curl not found, 18 fail streak) |
| Redis / MongoDB | healthy |
| Voice containers | 2 stopped (`jetson-wyoming:full-ram`, `jw-gpu`) |
| Repo on device | Not cloned |

### Steps Executed
1. Cloned repo: `git clone https://github.com/seiggy/lucia-dotnet.git ~/lucia-dotnet` → `master` @ `f484680`
2. Patched `Dockerfile.agenthost-jetson` on-device:
   - Added curl installation in base stage (fixes unhealthy healthcheck)
   - Stripped `@sha256:...` pins from all `FROM` lines (Docker 29.4.1 BuildKit fails on manifest-list digests)
3. Took down old stack: `docker compose -f ~/docker-compose.jetson.yml down --remove-orphans`
4. Built on-device: `docker compose -f infra/docker/docker-compose.jetson.yml build --no-cache lucia` (via nohup background script, ~15 min on A57)
5. Started stack: `docker compose -f infra/docker/docker-compose.jetson.yml up -d`

### Result
- Image built: `lucia:jetson` sha256:`4de9e2bb71f0`, 434MB
- New compose project: `docker` (from `lucia-dotnet/infra/docker/` directory)

### Validation
| Check | Result |
|-------|--------|
| `docker compose ps` — all healthy | ✅ lucia-jetson, lucia-mongo-jetson, lucia-redis-jetson all `(healthy)` |
| `curl http://localhost:7233/health` | ✅ `Healthy` |
| `redis-cli PING` | ✅ `PONG` |
| `mongosh ping` | ✅ `1` |
| Voice containers | ✅ Zero running |

### Access
- Dashboard / API: `http://192.168.1.239:7233`
- Health endpoint: `http://192.168.1.239:7233/health`
- Logs: `docker compose -f ~/lucia-dotnet/infra/docker/docker-compose.jetson.yml logs -f lucia`
- First-run setup wizard handles LLM keys and HA token — no `.env` needed

---

## Decisions / Action Items for Coordinator

### 1. Commit `deploy-jetson.sh` to remote (REQUIRED)
`infra/docker/deploy-jetson.sh` exists locally but was **never pushed** to `seiggy/lucia-dotnet`. The Jetson clone had it absent. Without this file, `deploy-jetson.sh` must be recreated manually every time. Coordinator should commit it.

### 2. Fix `Dockerfile.agenthost-jetson` SHA pins (REQUIRED before next deploy)
The `@sha256:...` digest pins added in PR #193 break on Docker 29.4.1 on the Jetson with:  
```
unexpected media type application/octet-stream for sha256:...: not found
```
Root cause: the SHAs were resolved as manifest-list (multi-arch index) digests, not single-platform ARM64 image digests. BuildKit on the Jetson can't resolve them.  

**Fix options (pick one):**
- **Option A (preferred):** Re-resolve digests for the Jetson Dockerfile specifically using `--platform linux/arm64`:  
  `docker buildx imagetools inspect --format '{{json .Manifest}}' mcr.microsoft.com/dotnet/sdk:10.0-noble-arm64v8 | jq '.digest'`  
  This gives the platform-specific digest, which BuildKit CAN resolve.
- **Option B:** Remove SHA pins from `Dockerfile.agenthost-jetson` only (the ARM64 image is already `*-arm64v8` tag, which is unambiguous). The other Dockerfiles can keep their pins.

The local `Dockerfile.agenthost-jetson` already has the curl fix (stage 1 of the needed changes).

### 3. Note: Volume data orphaned (informational)
Old stack volumes (`zackw_lucia-redis-data`, `zackw_lucia-mongo-data`) remain on disk but are no longer attached. They can be pruned with `docker volume prune` on the Jetson. No important data (first-run wizard config was not set up on the old unhealthy stack).

---

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

