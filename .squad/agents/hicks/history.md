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

<!-- Append new learnings below. -->
