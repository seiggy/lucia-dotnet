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

### Docker BuildX ARM64 Cross-Compile Feasibility Diagnostics (2026-07-17T14:49:46Z)
Validated Linux-ARM64 cross-build pipeline on Windows PC (Docker Desktop + .NET 10 SDK).

**Findings:**
- ✓ `.NET CLI restore/build/publish` succeeds for linux-arm64 RID with ExcludeSpeech=true
- ✓ Non-voice AgentHost (Jetson variant) builds reproducibly for ARM64
- ✓ Speech dependency gating (Wyoming.csproj) is comprehensive and correct
- ✗ Microsoft's ONNX Runtime GPU NuGet package: linux-x64 only (NO arm64 variant)
  - Confirms that Sherpa-ONNX C++ (not managed ORT) is necessary for Jetson voice
- ✗ Docker BuildX on this PC: no linux/arm64 QEMU emulation (x64-only)
  - Platform-specific SHA digest pinning fails when cross-compiling

**Validation Proven Locally:**
- Dockerfile.agenthost-jetson ExcludeSpeech gate is correctly designed
- Non-voice baseline is build-time reproducible
- Package selection logic handles ARM64 correctly

**Cannot Prove Without Physical Jetson:**
- Sherpa-ONNX C++ compilation on ARM64
- Container startup / NVIDIA runtime device access
- STT latency and thermal characteristics

**Status:** Diagnostics complete. Non-voice stack confirmed build-reproducible. Parallel findings from Brett (2026-07-17T14:49:02-04:00) merged into Decision 27. Awaiting physical hardware for voice stack validation.

**Learnings:**
1. **ONNX Runtime GPU NuGet is x64-only.** Microsoft does not distribute arm64 GPU binaries in NuGet; only x64. This is why Sherpa-ONNX C++ (not managed ORT) must be used for ARM64 CUDA support on Jetson. Design decision is proven correct.

2. **Docker BuildX on Windows Desktop cannot cross-compile for linux/arm64.** Platform-specific SHA digest pinning (e.g., `ubuntu:24.04@sha256:...arm64v8`) conflicts with amd64 BuildX instance. These digests are not multi-platform-index digests; they are platform-specific. Full Docker BuildX ARM64 support requires Linux host or removal of platform-specific digests from base images.

3. **.NET CLI is sufficient for build-time ARM64 validation.** NuGet package selection, cross-compilation RID logic, and dependency graphs all work correctly on Windows. No need for Docker QEMU; `dotnet publish -r linux-arm64` provides enough early validation.

4. **Speech dependency gating is tight.** Wyoming.csproj correctly removes speech source files (8 files) and conditionally includes packages based on ExcludeSpeech flag. Platform-specific logic (GPU for Linux, DirectML for Windows) works as designed.

5. **ARM64 build failure root cause is `Microsoft.ML.OnnxRuntime.Gpu.Linux` host-OS gating (Brett's empirical finding).** The package is referenced via `IsOSPlatform('Linux')` (build-HOST OS), not target RID. Windows hosts exclude it; Linux hosts include it. `Microsoft.ML.OnnxRuntime.Gpu.Linux` props attempt to copy `runtimes/win-arm64/native/onnxruntime.dll` (doesn't exist; x64-only package) on any `PlatformTarget==ARM64`, causing MSB3030. Smallest fix: gate on target RID `linux-x64` instead.

## Next Steps
- Coordinate with team for physical Jetson Orin Nano 8GB allocation
- PoC Stages 1–2: non-voice baseline validation, asset image build verification
- Once approved: Begin Stage 1 L4T baseline deployment
- No infrastructure changes until hardware success
