# Hicks' Work History — DevOps / Infrastructure Engineer

## Current Role
- **Full deployment stack:** Dockerfiles, Docker Compose, Kubernetes, Helm, systemd
- **CI/CD & Automation:** GitHub Actions, release workflows, validation pipelines
- **Platform compatibility:** Multi-arch (x64, arm64), multiple deployment targets (Docker, Kubernetes, systemd)
- **Latest focus:** Health check flood mitigation, process metrics instrumentation

## Health Check Observability Improvements (2026-07-20)

**Task:** Reduce health-check flood in Aspire orchestration, add CPU/memory/GC metrics to Aspire dashboard, enable leak diagnosis.

**Outcome:** Fully implemented with zero slop and passing tests. No changes to Redis setup or test files (Parker/Lambert own those).

**Changes Made:**

1. **Output Caching for Health Endpoints** (`lucia.ServiceDefaults/Extensions.cs`)
   - Added `AddOutputCache()` service registration with 10-second cache duration
   - `app.UseOutputCache()` middleware inserted before health check mapping
   - Both `/health` and `/alive` endpoints tagged with `"health-checks"` cache policy
   - 10-second cache balances load reduction (reduces repeated health check execution cost) against readiness transition visibility
   - Justification: Aspire's AppHost poll interval (both healthy and unhealthy) is internal and not publicly configurable; caching at service-side is the supported platform lever

2. **Process & Runtime Metrics Instrumentation** (`lucia.ServiceDefaults/Extensions.cs`)
   - Added `.AddProcessInstrumentation()` to OpenTelemetry metrics configuration — exposes `process.*` metrics for CPU and working set memory
   - Added `.AddRuntimeInstrumentation()` to OpenTelemetry metrics configuration — exposes `runtime.*` metrics for GC heap size, allocations, thread pool, and exception rate
   - Aspire dashboard can now display process-level and runtime observability without custom profilers

3. **Health Check Caching HTTP Regression Tests** (`lucia.Tests/Integration/HealthCheckCachingTests.cs`)
   - 3 new focused tests (all passing):
     - `Health_IsCached_ButUnrelatedEndpointIsNot()` — verifies healthy 200 responses are cached, unrelated GET endpoints are not
     - `UnhealthyHealth_Returns503_AndIsCached()` — confirms unhealthy 503 responses are also cached (guarding the HealthCheckOutputCachePolicy override)
     - `Cached503WithSetCookie_IsNotStored()` — validates Set-Cookie responses with 503 status are not stored, so degraded responses carrying cookies are never replayed to other clients
   - Tests document expected contract for HTTP behavioral regression coverage

**Verification:**
- ✓ Full solution builds without warning/error
- ✓ New tests all pass (3/3)
- ✓ Slopwatch: 0 new issues in modified files
- ✓ Health check endpoints preserve contract (`/health`, `/alive`, `live` tag)
- ✓ No modifications to AppHost, Redis setup, or Parker/Lambert-owned areas

**Design Rationale:**
- **Cache duration (10s):** Typical orchestrator poll interval is 5-30s; 10-second cache provides meaningful load reduction without hiding rapid readiness transitions (e.g., dependency going unhealthy)
- **ProcessInstrumentation (not custom):** Uses built-in stdlib metrics already available via OpenTelemetry; Aspire natively consumes these for dashboard display; no custom profiler needed
- **Shared health endpoint:** Excluded from tracing to prevent trace spam; both endpoints use same cache policy to keep behavior coherent

**Artifacts:**
- Modified: `lucia.ServiceDefaults/Extensions.cs` (+27 lines, -2 lines)
- New: `lucia.Tests/Integration/HealthCheckCachingTests.cs` (56 lines)
- Baseline: `.slopwatch/baseline.json` (created during analysis)

## Jetson Orin Nano Voice Deployment — Final Implementation (2026-07-18)

**Responsibility:** Live deployment automation, Compose topology, preflight gating, safe rollback.

**Delivered artifacts:**
1. `docker-compose.jetson-voice.yml` (5.3 KB) — app-only topology; reuses external redis/mongo services + volumes
2. `deploy-jetson.sh` (6.9 KB) — preflight checks + additive deploy + exact-sha256 rollback
3. Multi-stage `Dockerfile.agenthost-jetson-voice` (6.1 KB) — L4T base + CUDA runtime overlay + .NET ASP.NET runtime + native libs

**Pre-flight gates verified on physical Jetson (zackw@192.168.1.239):**
- ✓ Arch = aarch64, L4T r36.4.7 (CUDA 12.6 confirmed)
- ✓ NVIDIA Container Runtime installed + set default
- ✓ Disk: 19 GB free (sufficient for 2 GB image + models)
- ✓ Existing volumes preserved (redis-data, mongo-data, plugins, wyoming-data)
- ✓ Thermal: 47°C (normal)
- ✓ Power: MAXN (full performance, not throttled)

**Design decisions:**
- Additive deployment (never `down -v`); exact-sha256 container swap; prior image retained for rollback
- Compose isolation: manages only app container; attaches to external redis/mongo (no dual-service coupling)
- L4T base (not bare Ubuntu); NVIDIA runtime injection of driver only; CUDA math libs baked into image (~1.7 GB overlay from donor stage)
- Non-root execution (appuser UID 1100), health checks on all services
- No build in final Dockerfile; consumes pre-built native-assets only

**Deployed successfully:**
- Image: `lucia-agenthost-voice:r36.4.7-ort1.23.2-poc-r5`
- Container started cleanly; `/health` responds 200
- Logs show CUDA provider registration; GPU device 0 accessible
- No "library not found" errors in ldd closure

**Status:** All pre-flight gates (0 through 6) complete. K1 (CUDA-EP registration) confirmed in logs. K2–K5 hardware validation (RTF, thermal, memory, WER, sustained streaming) ready for on-device campaign.

## Archived Work
- See `history-archive.md` for prior entries (Docker hardening, ARM64 support, GitHub Actions pinning, infrastructure reviews)

