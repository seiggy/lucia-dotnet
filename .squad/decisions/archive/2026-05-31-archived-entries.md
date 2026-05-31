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
