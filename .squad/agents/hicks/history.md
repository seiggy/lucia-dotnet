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

- Participated in 2026-05-29 health review
---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.

### 2026-05-30: Docker Base Image Digest Pinning (PR #193, Issue #162)
- Pinned all 10 Dockerfiles to immutable base image digests (sha256 format) while retaining human-readable tags.
- Resolved digests for 10 unique base images: alpine:3.21, mcr.microsoft.com/dotnet/aspnet:10.0 (x2 variants), mcr.microsoft.com/dotnet/sdk:10.0 (x2 variants), node:22-alpine, node:22-slim, nvidia/cuda:12.6.3, rocm/dev-ubuntu-24.04:6.4.1-complete, rocm/onnxruntime.
- All Dockerfiles updated: main Dockerfile, a2ahost, agenthost-jetson, ha, assets, timer-agent, music-agent, voice, voice-cpu, voice-rocm (26 total line changes).
- Format: `FROM image:tag@sha256:<digest>` preserves tag for readability while pinning immutable digest.
- Supply-chain hardening: eliminates floating-tag risk, enables deterministic rebuilds, improves provenance tracking per charter requirement "pin exact versions".
- PR opened: https://github.com/seiggy/lucia-dotnet/pull/193
