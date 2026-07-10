# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** Docker, Kubernetes, Helm, systemd, GitHub Actions, .NET Aspire 13
- **Created:** 2026-03-26

## Key Systems I Own

- `infra/` — Full deployment stack
- Dockerfiles: 9 variants (agenthost, voice-gpu, voice-cpu, voice-rocm, HA, timer-agent, music-agent, a2ahost, assets)
- Docker Compose: standard + voice + sidecar configs
- Kubernetes: raw manifests + Helm chart (Redis, MongoDB, A2A deployments)
- systemd: lucia.service + install.sh
- Aspire AppHost: lucia.AppHost/ (dev orchestration)

## Deployment Modes

- **Standalone:** All agents in one AgentHost process
- **Mesh:** Separate A2A agent processes (timer-agent, music-agent via A2AHost)

## Learnings

### 2026-03-28: Docker Stack Hardening (PR #120, #119, #122)
- The **Dockerfile.ha pattern** (mkdir+chown+VOLUME) is the reference for any future production image — bake ownership into the image, not a runtime sidecar.
- **read_only: true + named volume mount** = correct security posture — the volume mount makes the specific path writable while keeping the rest of the FS read-only. Don't lift the read_only flag entirely.
- **mongo:8.0 + GLIBC_TUNABLES workaround** for kernel 6.19+ until upstream TCMalloc fix — server-side env var only, not needed on .NET driver side.
- **aspnet:10.0 ships curl, not wget** — always match healthcheck commands to what's actually installed in the base image.
- **Compose validation**: `docker compose config` catches YAML syntax errors early before full build.

### 2026-03-29: Jetson Nano ARM64 Support (Jetson Dockerfile + Compose)
- **ARM64 base images** use `-arm64v8` tag suffix; `mcr.microsoft.com/dotnet/aspnet:10.0-noble-arm64v8` is the recommended minimal base.
- **ExcludeSpeech=true** MSBuild property gates speech pipeline dependencies (ONNX Runtime, Wyoming); passed at restore, build, and publish stages.
- **No asset stage for Jetson** — removed assets/node-build pattern when no voice models are needed; simplifies build significantly.
- **Jetson resource constraints**: Tighter memory/CPU limits in docker-compose (Redis 128MB, MongoDB 256MB, AgentHost 512MB) for typical 4GB RAM boards.
- **Separate compose file pattern** (`docker-compose.jetson.yml`) keeps standard and Jetson deployments independent, reducing config complexity.

### 2026-05-29: Whole-Solution Infra & Build Review
- **squad-* workflows are wired to a phantom branch model.** They trigger on `main`/`dev`/`preview`/`insider`, but the only remote branch is `origin/master` (the default). Verified with `git branch -r`. None of squad-ci/release/preview/insider-release/promote ever fire on the branch that ships.
- **squad-ci/release/preview/insider-release are untouched scaffolding** — they `echo "No build commands configured"` instead of running `dotnet test`. No real CI/release gate exists via the squad suite.
- **squad-promote.yml reads the version from `node -e "require('./package.json').version"`** but there is NO root `package.json` (only `lucia-dashboard/`). The promote workflow fails on every run. .NET repos need a .NET version source, not a Node manifest.
- **asset.lock has no checksums and uses mutable refs** (`huggingface.co/.../resolve/main/...`, `raw.githubusercontent.com/.../main/...`). Content can change without the lock hash changing → reproducibility/supply-chain gap. CI does content-address the asset *image* via `sha-<hash>` though, which is solid.
- **validate-infrastructure swallows lint failures with `|| true`** (hadolint, yamllint, systemd-analyze) — false green. It also only lints 5 of 10 Dockerfiles (misses jetson/voice/voice-cpu/voice-rocm/assets) and only validates `docker-compose.yml` of 4 compose files.
- **Dev/prod drift in Aspire AppHost**: AppHost pins Mongo `7.0` while compose deploys `8.0.5`; appsettings.json sets `Store=PostgreSQL` while compose ships MongoDB. Worth re-checking on any data-layer task.
- Action pinning across all workflows uses mutable major tags (`@v6`/`@v4`), not SHAs — only Trivy (`@v0.35.0`) is version-pinned.
- Positive: the `aspnet:10.0 ships curl` learning held up; compose hardening (read_only/tmpfs/cap_drop/no-new-privileges) and the `sha-<hash>` asset-image pattern are reference-quality.

<!-- Append new learnings below. -->

### 2026-07-10: CUDA/ORT Version Compatibility — Issue #207 (PR squad/207-docker-voice-cuda)

**CONFIRMED:** `Dockerfile.voice` Stage 1 base image was pinned to `nvidia/cuda:12.6.3-cudnn-runtime-ubuntu24.04`
while the intended target (already documented in the Dockerfile header comment) was `12.8.1` — the FROM
line was never updated to match. The csukuangfj ORT 1.23.2 artifact repackages the upstream release by
changing SONAMEs rather than rebuilding; its exact runtime CUDA version requirement was not independently
verified from the artifact metadata.

**CONFIRMED:** CUDA 12.8 is required for full Blackwell (sm_120 / GB202) support. RTX 5090 users would
fail to initialise the CUDA device regardless of the ORT library loading behaviour.

**HYPOTHESIZED (unverified — needs GPU-host runtime test):** The exact mechanism producing
`cudaErrorInsufficientDriver` (error 35) at `cudaSetDevice()`. `cudaErrorInsufficientDriver` indicates the
loaded CUDA runtime cannot use the driver — distinct from a missing-symbol load failure. The true trigger
(version mismatch, Blackwell arch, or both) requires a physical GPU-host test to confirm.

**Fix:** Updated Stage 1 FROM to `nvidia/cuda:12.8.1-cudnn-runtime-ubuntu24.04@sha256:ac55d124da4882b497f732d8dfd9a702d5447a5f29d08d56da6f64f0a1eb34bc`.

**Validated (confirmed):** `docker build --target base` succeeded — new CUDA 12.8.1 base image pulls and
all base-stage layers build. This does NOT load the overlaid ORT GPU libs or call `cudaSetDevice()`.
Full runtime validation (ONNX CUDAExecutionProvider init on a physical GPU) still needed.

**CUDA/ORT Compatibility Matrix (ORT 1.23.x) — based on upstream docs, not locally verified:**
- ORT 1.23.2 + CUDA 12.8.x + cuDNN 9.x: Blackwell sm_120 ✅ (per NVIDIA release notes)
- ORT 1.23.2 + CUDA 12.6.x: Blackwell sm_120 ⚠️ fails; ORT runtime behaviour unverified

**Rule:** When bumping ORT GPU version, verify the CUDA base image against the upstream ORT release notes
and the csukuangfj artifact build logs/metadata to confirm the target CUDA version. `readelf -d` reports
major-version SONAMEs only (e.g. `libcudart.so.12`) and cannot distinguish minor versions.
Always keep `asset.lock` onnxruntime-gpu version and base image CUDA version in sync.
Host driver requirements vary by GPU family and CUDA minor version — consult
https://docs.nvidia.com/cuda/cuda-toolkit-release-notes/ rather than assuming a single minimum.

**CPU fallback:** `:voice-cpu` tag uses `mcr.microsoft.com/dotnet/aspnet:10.0` — no CUDA dependency.
Recommend documenting this as the explicit fallback for non-NVIDIA hosts or unsupported GPU generations.

### 2026-05-31: Jetson Non-Voice Deploy — RETRY SUCCESS (full on-device build & validate)

**Connectivity resolution**: The 192.168.0.x→192.168.1.x subnet gap from the previous run was resolved by Zack bouncing the Jetson. SSH to `zackw@192.168.1.239` connected cleanly with key-based auth (BatchMode=yes, ConnectTimeout=10). L3 ping RTT: 3–23ms.

**Live state found on Jetson before deploy**:
- Stack was running from `/home/zackw/docker-compose.jetson.yml` (project name `zackw`) using image `seiggy/lucia-agenthost:jetson` (pulled from registry, NOT built on-device).
- `lucia-jetson` was `unhealthy` (18 failing streak): `/bin/sh: 1: curl: not found` — the registry image was built without curl.
- `lucia-redis-jetson` and `lucia-mongo-jetson` were `healthy`.
- Two stopped voice containers (`jetson-wyoming:full-ram`, `jetson-wyoming-gpu`) present but not running.
- No git repo on the Jetson (`~/lucia-dotnet` did not exist).

**Deploy steps actually run**:
1. `git clone https://github.com/seiggy/lucia-dotnet.git ~/lucia-dotnet` → branch `master`, SHA `f484680`.
2. Patched `~/lucia-dotnet/infra/docker/Dockerfile.agenthost-jetson` on-device (Python3 in-place): added `RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*` in the base stage (curl absent from aspnet noble minimal image).
3. Second patch needed: stripped `@sha256:...` pins from all `FROM` lines — Docker 29.4.1 on Jetson throws `unexpected media type application/octet-stream` when resolving pinned manifest-list digests. Tags-only build succeeds natively on ARM64.
4. `docker compose -f ~/docker-compose.jetson.yml down --remove-orphans` — took down old `zackw` project stack (volumes preserved).
5. Built via background script (`nohup ~/run-lucia-deploy.sh`) writing to `~/lucia-build.log`:  
   `docker compose -f infra/docker/docker-compose.jetson.yml build --no-cache lucia`  
   then `docker compose -f infra/docker/docker-compose.jetson.yml up -d`
6. Build completed: image `lucia:jetson` sha256:`4de9e2bb71f0`, size 434MB.
7. Stack came up as project `docker` (from `lucia-dotnet/infra/docker/` directory name): three containers, all healthy within ~35s.

**Validation results**:
- `docker compose ps`: lucia-jetson (healthy), lucia-mongo-jetson (healthy), lucia-redis-jetson (healthy).
- `curl http://localhost:7233/health` → `Healthy`.
- `docker exec lucia-redis-jetson redis-cli PING` → `PONG`.
- `docker exec lucia-mongo-jetson mongosh --eval 'db.runCommand({ping:1}).ok'` → `1`.
- `docker ps | grep -Ei 'voice|wyoming|whisper|piper|wake'` → empty. Zero voice containers running.

**Gotchas / future infra notes**:
1. **`deploy-jetson.sh` not in remote repo** — the script exists only locally (not pushed to `seiggy/lucia-dotnet`). The Coordinator must commit `infra/docker/deploy-jetson.sh` so it's available on clone.
2. **SHA digest pinning breaks on Docker 29.4.1 on Jetson** for manifest-list digests. The pinned SHAs were resolved on a different host/platform and the Jetson's BuildKit can't resolve them. Either: (a) use `--platform linux/arm64` when resolving digests so they match the ARM64 image manifest (not the index), OR (b) strip pins from the Jetson Dockerfile and maintain a separate unpin step. The local `Dockerfile.agenthost-jetson` must be fixed before the next deploy (the curl fix is already committed locally but the SHA issue needs the coordinator to re-resolve or remove the ARM64 Dockerfile pins).
3. **Project name drift**: Old stack (project `zackw`) vs new stack (project `docker`) use different volume prefixes. Data in `zackw_lucia-mongo-data` etc. is orphaned — acceptable for fresh deploy, but worth noting if migration is ever needed.
4. **Note to self**: `aspnet:10.0 ships curl` learning from 2026-03-28 was wrong for the *registry-pulled* `seiggy/lucia-agenthost:jetson` image (it was built without curl). The base image DOES ship curl once apt-installed in the build stage.

### 2026-05-31: Jetson Non-Voice Deploy — Topology & Connectivity Findings

**Compose file**: `infra/docker/docker-compose.jetson.yml` — this is the canonical non-voice Jetson deploy file. Three services only: `lucia-redis-jetson` (Redis 8.2-alpine), `lucia-mongo-jetson` (MongoDB 8.0.5), `lucia-jetson` (ARM64 AgentHost built from `Dockerfile.agenthost-jetson`). No voice/STT/TTS/Wyoming services.

**Build approach (on-device)**: The compose file's `build.context: ../..` + `dockerfile: infra/docker/Dockerfile.agenthost-jetson` means Docker builds the image natively on the Jetson. The Dockerfile uses `mcr.microsoft.com/dotnet/sdk:10.0-noble-arm64v8` (ARM64 SDK — native, no emulation needed) and publishes with `--runtime linux-arm64`. Redis and MongoDB use official multi-arch images with native arm64 variants.

**ExcludeSpeech=true** is passed at restore, build, and publish stages in `Dockerfile.agenthost-jetson` — this gates out ONNX Runtime and Wyoming pipeline completely. The `Deployment__Mode` is not set, so it defaults to standalone (all agents embedded). No separate A2A containers needed for Jetson.

**Deploy command (on Jetson)**:
```bash
cd infra/docker
./deploy-jetson.sh          # first deploy (created 2026-05-31)
./deploy-jetson.sh --pull --rebuild  # update + clean rebuild
```

**Network blocker context**: This dev machine (192.168.0.x) cannot directly reach the Jetson (192.168.1.x) — different subnet, no route. The deploy-jetson.sh must be run from the Jetson directly or from a machine on the 192.168.1.x network. SSH key-based auth for `zackw@192.168.1.239` is required on whatever machine executes the SSH step.

**Key infra file paths for Jetson**:
- `infra/docker/docker-compose.jetson.yml` — non-voice compose (3 services)
- `infra/docker/Dockerfile.agenthost-jetson` — ARM64, no speech, pinned digest
- `infra/docker/deploy-jetson.sh` — one-command deploy script (NEW, 2026-05-31)
- `infra/scripts/health-check.sh` — post-deploy validation script

**Resource limits** (tight for 4GB Jetson): Redis 128MB, MongoDB 256MB, AgentHost 512MB / 1.5 CPU. No GPU/nvidia runtime required for non-voice.

**MongoDB 8.0 arm64**: Official `mongo:8.0.5` ships arm64 variant. The `GLIBC_TUNABLES=glibc.pthread.rseq=1` workaround for kernel 6.19+ TCMalloc crash is already in the Jetson compose.

### 2026-05-31: AppHost .env Loading & Seed Var Forwarding (Parker)
- **DotNetEnv + TraversePath() pattern**: Aspire AppHost does not auto-load `.env` files; use DotNetEnv 3.2.0 with `Env.NoClobber().TraversePath().Load()` before `CreateBuilder` to pick up repo-root `.env`.
- **WithEnvironment forwarding**: Aspire's `.WithEnvironment(name, value)` is the correct idiom for injecting AppHost-resolved env vars into child projects. Values become process environment variables, which `IConfiguration.AddEnvironmentVariables()` picks up.
- **Double-underscore vars in DotNetEnv**: Use `Environment.GetEnvironmentVariable()` directly, not `builder.Configuration[name]`, because the config provider normalizes `__` → `:`, making original names inaccessible.
- **Redis TLS health check fix (Aspire 13)**: `.WithoutHttpsCertificate()` on the Redis builder disables auto-TLS so the built-in health check can connect (AppHost dev cert is not trusted by the host's SslStream). This is documented opt-out; production Redis TLS is handled by Helm independently.

- Participated in 2026-05-29 health review

### 2026-05-31: Aspire 13 Redis Auto-TLS + Health Check EOF Fix

- **Aspire 13 (`Aspire.Hosting.Redis` 13.3.5) auto-enables TLS on the primary Redis endpoint at run time** when a dev certificate is present. Internally `AddRedis()` calls `WithHttpsCertificateConfiguration()` and `SubscribeHttpsEndpointsUpdate()`, which rewrites the primary endpoint from `redis://` to `rediss://` and starts Redis with `--tls-port 6379 --port 6380`.
- **The built-in `redis_check` health check** (registered via `builder.Services.AddHealthChecks().AddRedis(...)`) obtains the connection string from `ConnectionStringAvailableEvent` — which now resolves to `rediss://:{password}@localhost:<port>`. The `HealthChecks.Redis` `ConnectionMultiplexer` running in the AppHost process then tries a TLS handshake but the AppHost process does not trust the Aspire dev cert, causing `IOException: Received an unexpected EOF` at `SslStream.ReceiveHandshakeFrameAsync`.
- **Fix**: Add `.WithoutHttpsCertificate()` to the Redis builder chain in `lucia.AppHost/AppHost.cs`. This is the documented opt-out API in Aspire. It prevents `SubscribeHttpsEndpointsUpdate` from firing, keeping Redis on plaintext port 6379 so the health check connects cleanly. The method is marked `[Experimental("ASPIRECERTIFICATES001")]`, so `<NoWarn>$(NoWarn);ASPIRECERTIFICATES001</NoWarn>` was added to `lucia.AppHost/lucia.AppHost.csproj`.
- **Production posture unaffected**: `.WithoutHttpsCertificate()` is a run-time-only opt-out. Production Redis TLS is managed by the infra/Helm chart independently.
- **Key files**: `lucia.AppHost/AppHost.cs`, `lucia.AppHost/lucia.AppHost.csproj`.
- **Source**: [dotnet/aspire `RedisBuilderExtensions.cs`](https://github.com/dotnet/aspire/blob/main/src/Aspire.Hosting.Redis/RedisBuilderExtensions.cs) + [Aspire certificate-configuration docs](https://aspire.dev/certificate-configuration).

---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.

### 2026-05-30: Docker Base Image Digest Pinning (PR #193, Issue #162)
- Pinned all 10 Dockerfiles to immutable base image digests (sha256 format) while retaining human-readable tags.
- Resolved digests for 10 unique base images: alpine:3.21, mcr.microsoft.com/dotnet/aspnet:10.0 (x2 variants), mcr.microsoft.com/dotnet/sdk:10.0 (x2 variants), node:22-alpine, node:22-slim, nvidia/cuda:12.6.3, rocm/dev-ubuntu-24.04:6.4.1-complete, rocm/onnxruntime.
- All Dockerfiles updated: main Dockerfile, a2ahost, agenthost-jetson, ha, assets, timer-agent, music-agent, voice, voice-cpu, voice-rocm (26 total line changes).
- Format: `FROM image:tag@sha256:<digest>` preserves tag for readability while pinning immutable digest.
- Supply-chain hardening: eliminates floating-tag risk, enables deterministic rebuilds, improves provenance tracking per charter requirement "pin exact versions".
- PR opened: https://github.com/seiggy/lucia-dotnet/pull/193
## 2026-05-31 — PR #195 Workflow Hygiene

Cleaned up workflow configurations (squad-promote/preview/docs): step renames, workflow_dispatch migration, reduced permissions. Consolidated with Ripley/Parker into commit 9809a36.
