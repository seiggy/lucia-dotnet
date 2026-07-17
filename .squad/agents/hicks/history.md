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

### Off-Device Cross-Compile Build Strategy (2026-07-17T15:29:56Z)
Designed reproducible off-device pipeline for native ARM64/CUDA artifacts on Jetson (hard constraint: local RAM exhaustion).

**Strategy:**
- Native builder (Stage 0): x86 Linux with ARM64 cross-toolchain → ARM64 ELF binaries (ORT + Sherpa-ONNX)
- .NET publisher (Stage 1): Any host → linux-arm64 cross-publish (no emulation)
- Asset downloader (Stage 2): Alpine → voice models + binaries
- L4T runtime (Stage 3): `nvcr.io/nvidia/l4t-jetpack:r36.3` → final deployment

**Key Findings:**
1. **Pre-built artifacts preferred over source compile:** ORT/Sherpa pre-built tarballs (ARM64 + CUDA) available from NVIDIA/csukuangfj; no source compilation needed on builder
2. **SHA digest conflict identified & resolved:** Current `Dockerfile.agenthost-jetson` uses platform-specific SHAs (arm64v8 only); breaks multi-platform BuildX; solution: reference-only or multi-platform manifest index SHAs
3. **QEMU not required:** Cross-compile (FROM --platform=$BUILDPLATFORM) works without QEMU; native ARM64 toolchains sufficient
4. **.NET CLI proven sufficient:** Windows PC can cross-publish to linux-arm64 without emulation; NuGet handles RID logic
5. **Single Dockerfile design:** Consolidates 3 files (Dockerfile.agenthost-jetson, Dockerfile.voice, Dockerfile.assets) into 1 multi-stage build (Dockerfile.jetson-voice-prebuilt)
6. **Jetson receives only image:** No build tools, no compilers; purely OCI artifact load/pull

### Off-Device Cross-Compile Build Strategy: Dual-Path Edition (2026-07-17T15:33:59Z)
**Policy Clarification:** BuildX QEMU ARM64 emulation on 64GB desktop now explicit fallback when native cross-compile unavailable. Revised strategy with dual command paths.

**Updated Strategy:**
- **Path A (Preferred):** Native x64→ARM64 cross-compile (fast, uses gcc-aarch64-linux-gnu)
- **Path B (Fallback):** QEMU ARM64 emulation on 64GB desktop (slower, acceptable, never on Jetson)
- **Hard Constraint:** Never move compilation back to 8GB Jetson (explicitly forbidden)

**Dual-Path Implementation:**
1. Single `Dockerfile.jetson-voice-prebuilt` with both paths (auto-selects via BuildX --platform)
2. Conditional wrapper script: `build-native-jetson.sh` (detects cross-toolchain, selects Path A or B)
3. Full multi-stage build command: orchestrates all 4 stages with appropriate path

**Key Decision:** Pre-built artifacts (ORT 1.18.1, Sherpa 1.20.2) use Path A (native cross-compile). If source rebuild needed (e.g., incompatible pre-built), Path B (QEMU) available on desktop; 64GB RAM >> 8GB Jetson memory budget; acceptable overhead.

**Rationale:**
- Path A faster when cross-toolchain available (typical CI/CD Linux)
- Path B fallback on Windows PC (no cross-toolchain) or when source compilation unavailable
- Policy explicitly permits QEMU on desktop, forbids it (and any build) on Jetson
- Same ARM64 ELF output from both paths; only build method differs

## Learnings (Expanded)
1. **Dual-path build strategy is practical:** Native cross-compile (fast) + QEMU fallback (slow but available) allows flexibility without ever moving compilation to constrained Jetson. Desktop's 64GB memory budget tolerable for emulation overhead.

2. **Policy isolation prevents mistakes:** Explicit directive prohibiting Jetson builds (even QEMU on Jetson) forces off-device discipline. CI/CD + desktop = safe zones; Jetson = load/run only.

3. **Pre-built artifacts >> source compile:** ORT/Sherpa pre-built tarballs for ARM64+CUDA available; source compilation only when pre-built breaks (rare). Default to Path A (cross-compile pre-builts), fallback to Path B (QEMU source, if needed).

4. **Dockerfile logic is build-path-agnostic:** Both paths produce identical ARM64 ELF output; Dockerfile doesn't need to know which path was used. BuildX --platform selection automatically chooses available path (cross-compile if toolchain present, QEMU otherwise).

5. **Wrapper scripts automate path selection:** `build-native-jetson.sh` detects cross-toolchain availability and logs which path was selected, eliminating manual decision overhead and preventing Jetson builds.

## Learnings
1. **Off-device compilation is mandatory for Jetson Nano Super 8GB.** Source compile of Sherpa-ONNX + ORT exhausts RAM; pre-built tarballs are the only practical path. This is not a workaround—it's the intended use case for resource-constrained devices. Always download pre-built ARM64 artifacts first; only consider source compilation as fallback with sufficient RAM or swap.

2. **Platform-specific SHA digests break multi-platform BuildX.** Base images published with separate arm64v8 SHAs (not multi-platform manifest indices) conflict when BuildX on amd64 tries to resolve them. Solution: use reference-only tags (lighter, repull on multi-platform builds) or confirm multi-platform manifest indices available. For production pinning, use local cache or registry cache (BuildX --cache-from/--cache-to).

3. **Cross-compile requires no QEMU for .NET + Docker BuildX.** .NET SDK handles ARM64 RID logic natively (dotnet restore/publish). Docker BuildX with FROM --platform=$BUILDPLATFORM avoids QEMU for static cross-compilation (no runtime needed). Full ARM64 image builds still need QEMU or Linux host, but native artifact compilation and .NET publish do not.

4. **Single consolidated Dockerfile enforces version consistency.** Splitting native builder, .NET publisher, and L4T runtime into separate files (current state) risks version skew (e.g., ORT 1.18 built with Sherpa 1.20 compiled for different CUDA). Single multi-stage Dockerfile with atomic version bumping (jetson-artifacts.lock) is mandatory for reproducibility.

5. **Pre-built architecture isolation prevents mistakes.** Stages 0-2 (native, publisher, assets) are independently buildable; Stage 3 (final image) just COPYs them. This isolation prevents accidental recompilation or version mixing. CI/CD can cache Stages 0-2 independently and only rebuild Stage 3 when app code changes.

## Next Steps
- Coordinate with team for physical Jetson Orin Nano 8GB allocation
- Create `Dockerfile.jetson-voice-prebuilt` + `jetson-artifacts.lock` + updated deploy script
- PoC Stages 1–5: non-voice baseline → native artifacts → STT latency → thermal → stress test
- Once approved: Begin Stage 1 L4T baseline deployment
- No infrastructure changes until hardware success

### Off-Device Build Infrastructure: Dual-Path Refinement (2026-07-17T15:33:59Z)
Designed a multi-stage Dockerfile consolidating three existing files (Dockerfile.agenthost-jetson, Dockerfile.voice, Dockerfile.assets) with dual build paths: native x64→arm64 cross-compile (preferred, fast) and QEMU emulation (fallback on 64GB desktop). Enforced hard constraint: never compile on 8GB Jetson. Rejected inaccurate preliminary proposals (nvidia-smi probes, invented thermal targets, latest tags, unsupported L4T claims, unverified multi-file design). Coordinated with Brett's ORT version alignment findings to resolve API mismatch between sherpa and managed ORT. Updated strategy to incorporate Brett's authoritative artifact validation and primary-source checksums.

**Key coordination:** Brett's findings revealed that preliminary Hicks proposal had incorrect ORT 1.18.1 URL and version-conflict assumption. Decision 28 reflects Brett's corrections as authoritative.

**Status:** Infrastructure design approved; Dockerfile implementation pending Brett's artifact validation and Zack's PoC hardware allocation.
