# Hicks' Work History — DevOps / Infrastructure Engineer

## Current Role
- **Full deployment stack:** Dockerfiles, Docker Compose, Kubernetes, Helm, systemd
- **CI/CD & Automation:** GitHub Actions, release workflows, validation pipelines
- **Platform compatibility:** Multi-arch (x64, arm64), multiple deployment targets (Docker, Kubernetes, systemd)
- **Latest focus:** Jetson Orin Nano deployment research and infrastructure correctness

## Key Systems Owned
- `infra/` — Full deployment stack (9 Dockerfile variants, Compose configs, Kubernetes/Helm, systemd, Aspire)
- Dockerfiles: agenthost, voice-gpu, voice-cpu, voice-rocm, HA, timer-agent, music-agent, a2ahost, assets
- Docker Compose: standard + voice + sidecar configs
- Kubernetes: raw manifests + Helm chart (Redis, MongoDB, A2A deployments)
- systemd: lucia.service + install.sh
- Aspire AppHost: lucia.AppHost/ (dev orchestration)
- GitHub Actions: squad-* workflows, validation pipelines

## Deployment Patterns
- **Standalone:** All agents in one AgentHost process
- **Mesh:** Separate A2A agent processes (timer-agent, music-agent via A2AHost)
- **Multi-arch:** x64 CUDA, arm64 (CPU), Jetson (ExcludeSpeech), potential ROCm variants

## Recent Work (2026-07)

### Jetson Orin Nano Deployment Architecture (2026-07-17 — Research, Corrected)
Initial deployment proposal reviewed and corrected by Ripley (lead architect). Corrections applied:

**Rejected proposals:**
- NVIDIA Riva (over-engineered, license cost, memory pressure)
- Triton (viable but overkill; adds HTTP/gRPC layer without benefit)
- Python-Whisper CPU fallback (violates user directive)
- 8 speculative new infra files (reuse existing assets instead)

**Corrected facts:**
- ORT: 1.18.1 aarch64 GPU tarball (not Python wheel)
- Orin Nano 8GB: 1024 CUDA cores, 32 tensor cores (not 512)
- Monitoring: use `tegrastats` (Jetson), not `nvidia-smi` (desktop)
- Deployment: L4T rootfs flash (not ISO)

**Recommended path:** Sherpa-ONNX + native ORT GPU (L4T + Docker Compose + systemd) — minimal viable, Ponytail-aligned.

**Status:** Preliminary proposal corrected. No code changes. Awaiting coordinator approval for PoC (3–4 weeks on physical hardware).

## Archived Work
- See `history-archive.md` for prior entries (Docker hardening, ARM64 support, infra reviews, CUDA/ORT compatibility, GitHub release workflows and security corrections)

## Next Steps
- Coordinate with team for physical Jetson Orin Nano 8GB allocation
- PoC Stages 1–2: non-voice baseline validation, asset image build verification
- No infrastructure changes until hardware success
