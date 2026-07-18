# Hicks' Work History — DevOps / Infrastructure Engineer

## Current Role
- **Full deployment stack:** Dockerfiles, Docker Compose, Kubernetes, Helm, systemd
- **CI/CD & Automation:** GitHub Actions, release workflows, validation pipelines
- **Platform compatibility:** Multi-arch (x64, arm64), multiple deployment targets (Docker, Kubernetes, systemd)
- **Latest focus:** Jetson Orin Nano deployment implementation and final artifact delivery

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

