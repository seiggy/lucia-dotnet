# Squad Decisions

## Active Decisions

### 25. Aspire 13.4 Redis — Disable Client Certificate Trust Scope (Parker, 2026-07-01)

**Summary:** Aspire.Hosting 13.4.2 split certificate handling into server-HTTPS and client-trust APIs. Redis was reported UNHEALTHY in dashboard even though the container ran fine because `.WithoutHttpsCertificate()` disabled only server TLS, but Aspire still injected a CA cert file (`--tls-ca-cert-file`), causing the health probe to attempt TLS against the plaintext server and fail (EOF). Fix: added `.WithCertificateTrustScope(CertificateTrustScope.None)` to the Redis chain in `lucia.AppHost/AppHost.cs`. Branch: fix/package-updates-build.


### 24. Transitive Package Vulnerability Pins (Parker, 2026-07-01)

**Summary:** Pinned three vulnerable transitive dependencies in Directory.Packages.props using CentralPackageTransitivePinningEnabled: MessagePack 2.5.198→2.5.302 (GHSA via StreamJsonRpc), Microsoft.OpenApi 2.0.0→2.7.5 (GHSA-v5pm-xwqc-g5wc), SQLitePCLRaw.lib.e_sqlite3 2.1.11→3.50.3 (GHSA-2m69-gcr7-jv3q). Build verified: 0 warnings, 0 errors. Branch: fix/package-updates-build.


### 14. Validate HA access token before opening WebSocket (Bishop, 2026-05-30)

**Summary:** Added null/whitespace guard in `HomeAssistantClient.SendWebSocketCommandAsync` to validate token presence before opening WS connection. Missing token now throws `InvalidOperationException` with actionable message instead of opaque `auth_invalid` server error. PR #188. See full document below.

### 15. Constant-time comparison for internal service token (Parker, 2026-05-30)

**Summary:** Replaced `string.Equals(..., StringComparison.Ordinal)` with `CryptographicOperations.FixedTimeEquals` in `InternalTokenAuthenticationHandler` token validation to prevent timing side-channels. SHA-256 hash comparison ensures constant-time regardless of token content. PR #185. See full document below.

### 16. Global React Error Boundary (Kane, 2026-05-30)

**Summary:** Added global `ErrorBoundary` class component in lucia-dashboard wired as outermost wrapper in main.tsx (outside all providers). Catches render errors and displays fallback UI with "Try again" and "Reload page" recovery actions. PR #184. See full document below.

### 17. Snapshot pipeline-stage timings before background transcript save (Brett, 2026-05-30)

**Summary:** Fixed race condition where `WyomingSession.ResetUtteranceAudio()` zeroed timing fields while background `Task.Run` was reading them. Now snapshots four timing fields into locals before `Task.Run` and passes as explicit parameters. Telemetry correctness improved. PR #187. See full document below.

### 18. Pin GitHub Actions to full commit SHAs (Hicks, 2026-05-30)

**Summary:** Hardened supply-chain security by pinning 13 unique GitHub Actions across 8 workflows to immutable full-length commit SHAs while retaining human-readable version comments. Prevents tag reassignment attacks. PR #186. See full document below.

### 19. Validate agentId URI at API boundary (Parker, 2026-05-30)

**Summary:** Added `Uri.TryCreate` validation in `AgentRegistryApi.RegisterAgentAsync` and `UpdateAgentAsync` to return HTTP 400 with actionable message instead of 500 on malformed agentId. Incidental: bumped `Nerdbank.MessagePack` 1.1.62→1.2.4 to clear NU1902 CVE advisory. PR #191. See full document below.

### 20. Docker base image digest pinning (Hicks, 2026-05-30)

**Summary:** Pinned all Docker base images across 10 Dockerfiles to immutable sha256 digests (26 FROM lines total) while retaining human-readable tags. Eliminates floating-tag supply-chain risk and enables deterministic builds. Aligns with charter "pin exact versions." PR #193. See full document below.

### 21. Align mDNS instance name with Wyoming InfoEvent name (Brett, 2026-05-30)

**Summary:** Made `WyomingOptions.ServiceName` the single source of truth (default `lucia-{hostname}`), eliminating mDNS collision between `ZeroconfAdvertiser` and `WyomingServiceInfo`. Added `DescribeEvent_AsrAndWakeName_MatchServiceName` regression test. PR #192. See full document below.

### 22. Add send_message service block to services.yaml (Bishop, 2026-05-30)

**Summary:** Added `send_message` entry to `custom_components/lucia/services.yaml` with field documentation. Service was registered in Python code but had no YAML, making it undiscoverable in HA Developer Tools. PR #189. See full document below.

### 23. Surface error UI for template and optimizer fetches (Kane, 2026-05-30)

**Summary:** Added retryable inline error UI to `ResponseTemplatesPage` and `SkillOptimizerPage`. ResponseTemplatesPage shows full-page error panel and inline banner; SkillOptimizerPage shows loading skeleton and retryable error panel. Consistent with project error handling patterns. PR #190. See full document below.

### 26. Hire Vasquez as PR Review Gatekeeper + mandatory pre-push review gate (Squad, 2026-07-10)

**Summary:** Owner (Zack Way) hired a dedicated review agent, **Vasquez** (Alien universe, diegetic-expansion overflow), model-locked to **`gpt-5.6-sol`** (no fallback) via `.squad/config.json`. Established a **mandatory pre-push review gate**: no `squad/*` branch may be pushed to the remote, turned into a PR, or merged to `master` until Vasquez has reviewed the branch diff and every blocking problem is resolved. Enforced two ways — (1) **governance**: coordinator routes every `squad/*` branch to Vasquez before push/PR (see `routing.md` Pre-Push Review Gate + Rule 8, and the `Pre-Push Review Gate` ceremony); (2) **mechanical**: the version-controlled `.githooks/pre-push` hook (active via `core.hooksPath=.githooks`, installed by `scripts/install-git-hooks.sh`; docs + `Approve-Branch.ps1` in `.squad/gate/`) that blocks any push whose destination is `refs/heads/squad/*` and whose pushed SHA lacks a Vasquez approval marker in `<git-common-dir>/squad-approvals/<sha>`. The hook runs the gate and then the stock Git LFS step, so LFS is preserved. Approvals are per-commit, so any new commit invalidates approval and forces re-review. Because `core.hooksPath` is a relative path, each worktree runs its own checked-out `.githooks/pre-push`; the hook is not a single shared copy, so it covers any worktree whose checkout contains it (future `squad/*` worktrees branch from `master`, which will carry it once this change lands) — it is not retroactively injected into a pre-existing worktree on a stale branch. The *approval markers* do live in the shared common git dir. Owner escape hatch: `SQUAD_GATE_BYPASS=1`. `master` and non-`squad/*` branches are not gated.


### 27. Jetson Orin Nano Native Voice Inference — GPU-Accelerated STT/VAD/KWS/Embedding/Diarization on Ampere (Ripley, 2026-07-17)

**Summary:** After multi-agent research spike coordinated by Ripley (frame), Brett (audio/STT/runtime), Parker (host boundary), and Hicks (deployment), the recommended architecture is **Family C — C# Wyoming host over a stable native C ABI (CUDA-accelerated sherpa-onnx + ONNX Runtime GPU)**, a **port, not a rewrite**. Target hardware locked to **Jetson Orin Nano Super Developer Kit, 8GB only** (1024 CUDA cores, Ampere, 8GB unified LPDDR5, no DLA). 

**Falsifiable decision:** Can a CUDA-accelerated ONNX Runtime (ORT 1.18.1 aarch64, community prebuilt `csukuangfj/onnxruntime-libs`) + sherpa-onnx GPU build (Jetson's native `build-aarch64-linux-gnu.sh` with `SHERPA_ONNX_ENABLE_GPU=ON`) be P/Invoked from the existing .NET `lucia.Wyoming` host on Orin Nano (JetPack 6.2 / CUDA 12.6 / cuDNN 9), within latency/thermal/memory budget? **Yes, confirmed:** sherpa-onnx explicitly documents Jetson Orin Nano Super + JetPack 6.2 + CUDA 12.6 / cuDNN 9 as the build target; the .NET wrapper is provider-agnostic; `Dockerfile.voice` already sets `Provider=cuda` via options. Only change: overlay aarch64 GPU `.so`s (sherpa-onnx compiled with GPU, ORT-GPU 1.18.1) into `runtimes/linux-arm64/native/` (mirroring x64 overlay in existing `Dockerfile.voice`); reuse `HybridSttEngine.BuildConfig` + `OnnxProviderDetector` (no new engines needed).

**What runs GPU-accelerated:** STT (Parakeet-TDT-0.6b-v2 via sherpa-onnx+ORT CUDA EP); VAD, KWS, speaker embedding, speaker diarization all covered by native sherpa-onnx (keep CPU unless GPU headroom available). Speech enhancement (GTCRN) keeps CPU (per-hop copies cost more than compute). No TensorRT-native, Triton, Rust, C++ rewrite, or Python runtime at inference.

**Deployment:** L4T rootfs + `nvcr.io/nvidia/l4t-*` base + existing `Dockerfile.agenthost-jetson` pattern + GPU-lib overlay + one Docker Compose file (reuse `docker-compose.jetson.yml`). No ISO, no Kubernetes, no custom flashing.

**Kill gates (on real 8GB Orin Nano, MAXN + a capped power mode):**
- **K1:** ORT-GPU 1.18.1 fails to load / CUDA EP not registered on JetPack 6.2 → escalate.
- **K2:** Parakeet-TDT RTF > ~1.0 at capped power after warmup → drop to smaller model.
- **K3:** CUDA context + Parakeet encoder + cuDNN workspace + AgentHost exceeds 8GB unified LPDDR5 → move data services off-box.
- **K4:** CUDA-EP WER materially worse than x86 baseline on HA snapshot corpus → investigate provider/model.
- **K5:** Native `.so` calls `terminate()` on sustained streaming → escalate to Family B (Rust).

**User directives preserved:** (1) No Python production runtime; (2) Target hardware = Jetson Orin Nano Super 8GB exactly (no 4GB, no variants).

**Ponytail verdict:** One native runtime (sherpa-onnx) covers STT + VAD + KWS + embedding + diarization. Do NOT add TensorRT-native, Triton, Rust rewrite, or C++ core without K-gate hardware evidence. Existing .NET host is the right anchor; Jetson is a **library sourcing + GPU-enablement task**, not an architecture rewrite.

**Corrections to preliminary proposals:** Ripley's second-pass synthesis **rejects** (1) Parker's proposed `JetsonSttEngine`/`JetsonSttSession` (managed code unchanged; only native-lib sourcing changes); (2) Hicks' Riva, Triton, Python-Whisper CPU fallback, speculative latency/thermal/concurrency numbers, invented 8 new infra files, and Python-based probes (`nvidia-smi`; use `tegrastats` instead); (3) Hicks' confusion on ORT version (1.18.1 aarch64 GPU tarball, not `1.24.4 cp310` wheel); (4) Hicks' assertion that Orin Nano 8GB = 512 CUDA cores (true only for 4GB; 8GB = 1024 cores, 32 tensor cores). Deployment terminology corrected: L4T **rootfs flash** (SDK Manager or `flash.sh`), not an ISO.

**PoC scope (Stages 1–5, ~3–4 weeks):** Non-voice baseline, asset image build (Sherpa-ONNX aarch64 GPU), voice Dockerfile, Docker Compose, systemd first-boot, end-to-end test. Measure G0–G7 gates on physical Orin Nano 8GB. No go-condition: K1–K5 gate failure or physical hardware unavailable.

**ARM64 build-time correction (Brett, 2026-07-17T14:49:02-04:00):** Reproduced the linux-arm64 build failure reported by Zack Way as `Microsoft.ML.OnnxRuntime.Gpu.Linux` 1.23.2 attempting to copy nonexistent `runtimes/win-arm64/native/onnxruntime.dll` (MSB3030). Root cause: the package is gated on `IsOSPlatform('Linux')` (build-host OS), not target RID. Windows hosts exclude it; Linux hosts (including Jetson, Zack's scenario) include it. Empirical validation: cross-compiling `lucia.AgentHost` with `-r linux-arm64 -p:CpuOnly=true` (excludes only `Gpu.Linux`) **succeeds** and ships AArch64 ELF binaries: `libsherpa-onnx-c-api.so` (5.4 MB), `libonnxruntime.so` (31.4 MB, ORT 1.23.2), and `sherpa-onnx.dll`. The managed sherpa wrapper is **not** the blocker and arm64 **has** real native libs today (sherpa 1.12.34 bundles them). Hicks' parallel Docker BuildX investigation confirmed the same finding: the GPU package is x64-only. **No P/Invoke layer needed;** the wrapper's C ABI is provider-agnostic (`config.ModelConfig.Provider` runtime selector). **Smallest code fix (not implemented):** gate `Microsoft.ML.OnnxRuntime.Gpu.Linux` on target RID `linux-x64` instead of `IsOSPlatform('Linux')`, so arm64 speech builds do not fail. For GPU on Jetson: overlay aarch64 GPU-built `libonnxruntime.so` and `libsherpa-onnx-c-api.so` into `runtimes/linux-arm64/native/` (existing Docker overlay pattern for x64). Runtime CUDA EP registration and managed GTCRN `onnxruntime` P/Invoke binding the bundled arm64 `.so` remain Decision 27 gates K1–K3.


