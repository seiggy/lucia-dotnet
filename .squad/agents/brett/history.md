# Brett's Work History — Voice / Speech Engineer

## Current Role
- **Voice pipeline specialist:** STT engines (HybridSttEngine, SherpaSttEngine, GraniteOnnxEngine), VAD, diarization, model management
- **Key systems:** `lucia.Wyoming/`, sherpa-onnx integration, ONNX Runtime inference sessions
- **Latest focus:** Jetson Orin Nano GPU-accelerated voice pipeline research

## Key Constraints & Patterns
- SherpaOnnx.DenoisedAudio has Dispose() but does NOT implement IDisposable — use explicit try/finally
- Audio pipeline latency is critical — measure at every stage
- Model IDs must be filesystem-safe (no "..", no path separators)
- All processing is local — no cloud dependencies for voice

## Recent Work (2026-07)

### Jetson Orin Nano Native Voice Stack (2026-07-17 — Research)
Validated the exact JetPack 6.2 sherpa-onnx aarch64 GPU build, ORT 1.18.1 community dependency, Parakeet-TDT-v2 model, CPU/GPU stage placement, licensing, and benchmark gates.

**Key findings:**
- ORT 1.18.1 aarch64 (community prebuilt, not ≥1.20)
- GPU-accelerated stages: STT (Parakeet-TDT via CUDA EP)
- CPU-only: Speech enhancement (GTCRN; per-hop copies cost more than compute)
- All staging via sherpa-onnx native library
- Model licensing verified (Parakeet-TDT CC-BY-4.0; Silero VAD MIT; 3D-Speaker Apache-2.0)
- Kill gates (B1–B4): ORT load, RTF, memory budget, WER

**Status:** Research complete, input merged into Decision 27. No code changes.

### sherpa wrapper arm64 diagnosis (2026-07-17 — follow-up for Zack)
Diagnosed Zack's "unsupported CPU architecture" arm64 build failure. See inbox
`brett-jetson-sherpa-wrapper-correction.md`.

**Durable learnings:**
- `org.k2fsa.sherpa.onnx` is a managed-only wrapper; natives live in per-RID
  `org.k2fsa.sherpa.onnx.runtime.<rid>` packages. `runtime.linux-arm64` 1.12.34 ships real
  AArch64 `libsherpa-onnx-c-api.so` + `libonnxruntime.so`. Wrapper is NOT the arm64 blocker;
  no hand-written P/Invoke needed.
- Root cause of the arm64 build break: `Microsoft.ML.OnnxRuntime.Gpu.Linux` (x64-only) props
  copy `runtimes/win-arm64/native/onnxruntime.dll` (no Exists() guard) on any `PlatformTarget==ARM64`
  → `error MSB3030 ... not found`. It's referenced via `IsOSPlatform('Linux')` (build-HOST OS),
  so Windows builds skip it (false pass) but Linux/arm64 builds break. Fix = gate on target RID
  `linux-x64`, not host OS.
- Verified on this PC: `dotnet publish lucia.AgentHost -r linux-arm64` (speech ON) SUCCEEDS on
  Windows and ships AArch64 ELF (`e_machine=183`) sherpa + ORT natives. Windows publish can't
  reproduce Zack's Linux-host failure because the culprit package is host-OS-gated.
- sherpa-onnx 1.12.34 bundles ORT **1.23.2** (arm64 .so version string) — same as
  `Microsoft.ML.OnnxRuntime.Managed` 1.23.2. One shared arm64 `libonnxruntime.so` can serve both
  sherpa STT and the managed GTCRN path; GTCRN needs no separate arm64 ORT NuGet (none exists).
  Verify managed `onnxruntime` P/Invoke resolves sherpa's bundled .so on-device.
- C ABI is provider-agnostic (official `c-api.h`: `const char *provider; // "cpu","cuda","coreml"`).
  GPU on Jetson = native `.so` overlay only (mirror x64 `Dockerfile.voice`), managed unchanged.

**Status:** Merged into Decision 27 with Hicks' parallel BuildX findings (2026-07-17T14:49:46Z).

### Off-device ORT native + API-version conflict (2026-07-17T15:29:05-04:00 — Zack)
Off-device native-toolchain feasibility for the "Jetson must not compile locally" constraint.
Empirically verified on this x64 Windows PC via Docker BuildX (`--output type=cacheonly`,
`--no-cache`, `FROM --platform=$BUILDPLATFORM debian:bookworm-slim`). See inbox
`brett-jetson-ort-version-alignment.md`.

**Durable learnings:**
- x64→arm64 **cross-compile needs no QEMU**: on an `x86_64` build host, `aarch64-linux-gnu-g++`
  emits `ELF 64-bit LSB … ARM aarch64`. sherpa's `build-aarch64-linux-gnu.sh` @v1.12.34 is exactly
  this (requires `aarch64-linux-gnu-*`, exits otherwise) and **fetches prebuilt ORT** (never builds
  ORT from source). QEMU is only the fallback per Directive 2026-07-17T15:31:40Z.
- **ORT C-API version conflict is a real blocker Decision 28 stepped on.** `Microsoft.ML.OnnxRuntime.Managed`
  1.23.2 calls `OrtGetApiBase()->GetApi(23)`; a **1.18.1 native returns null** (ORT_API_VERSION=23 in
  the 1.23.2 header; OrtApi is append-only). So managed 1.23.2 + native 1.18.1 = GTCRN/OnnxProviderDetector
  init fails. A single deployed `libonnxruntime.so` must satisfy BOTH the managed ORT path (needs API≥23)
  AND sherpa (built for 1.18, requests API 18 — OK via back-compat). Two `libonnxruntime.so` in one
  process is not viable (single soname).
- **Both arm64 CUDA ORT natives ship a standalone, managed-consumable `libonnxruntime.so` + providers**
  (verified by extracting each off-device):
  - csukuangfj v1.18.1 `onnxruntime-linux-aarch64-gpu-cuda12-1.18.1.tar.bz2` (51,594,609 B, **SHA256
    `1e91064ec13a6fabb6b670da8a2da4f369c1dbd50a5be77a879b2473e7afc0a6` verified**): `libonnxruntime.so
    → .so.1.18.1` (20.5 MB) + `_providers_cuda.so` (325 MB) + `_tensorrt.so` (840 KB) + `_shared.so`.
    Hash-pinned/documented = sherpa's blessed version. **Requires downgrading managed ORT→1.18.1 on arm64.**
  - guyin24 `onnxruntime_gpu-1.23.2-cp310…linux_aarch64.whl` (115,821,330 B; tag v1.24.4): ships a real
    standalone `onnxruntime/capi/libonnxruntime.so.1.23.2` (25 MB) + `_providers_cuda.so` (171 MB) +
    `_tensorrt.so` (904 KB) + `_shared.so` (+ pybind .so, unused for .NET). **No-downgrade path**: keep all
    managed ORT at 1.23.2. Community provenance (not sherpa-hash-pinned); needs deploy-time
    `libonnxruntime.so` + `libonnxruntime.so.1.18.1` symlinks; back-compat is a K-gate on-device.
- **Coordination fix for Hicks' Decision 28:** its URL `…/v1.18.1/onnxruntime-linux-aarch64-gpu-1.18.1.tgz`
  **does not exist** (verified via `gh api …/releases/tags/v1.18.1`); the only aarch64 GPU asset is the
  `…-gpu-cuda12-1.18.1.tar.bz2` above. Also Decision 28 pins native 1.18.1 while keeping managed 1.23.2 —
  that mix is broken until managed ORT is aligned to 1.18.1 on arm64 (or the native is moved to 1.23.2).
- csukuangfj aarch64 **GPU** ORT stops at 1.18.x (its v1.23.2 aarch64 asset is **CPU-only**); the only
  off-device source of a 1.23.x aarch64 **CUDA** native is community Jetson wheels (guyin24 / jetson-ai-lab).
- The managed sherpa wrapper (`sherpa-onnx.dll` 1.12.34) stays **unchanged** in every path; it P/Invokes the
  sherpa C API, not ORT, so it is ORT-version-agnostic.
- Build-path policy (Directive 2026-07-17T15:31:40Z): **native x64→arm64 cross-compile preferred** (proven
  here, no QEMU); **QEMU emulation is the supported desktop fallback**, never Jetson. Emulation is not
  automatic — verified on this PC that `--platform linux/arm64` `RUN` gives `exec format error` until
  `docker run --privileged --rm tonistiigi/binfmt --install arm64` registers `qemu-aarch64` (then arm64 RUN
  prints `aarch64`). The current stack (sherpa v1.12.34 cross script + ORT prebuilt fetch + `dotnet publish
  -r linux-arm64`) fits the cross path, so QEMU is a safety net, not a requirement.

**Status:** Research/diagnostics only. No code/package changes. Recommendation + benchmark checklist in
final memo; correction filed to inbox for Decision 27/28.

### Off-Device Build Strategy: ORT Version Alignment (2026-07-17T15:29:05-04:00)
Resolved a critical ORT version-conflict between sherpa-onnx (1.18.1) and managed `Microsoft.ML.OnnxRuntime` (1.23.2). Documented two viable production tracks: Track A (pin managed 1.18.1 for linux-arm64 only, zero skew) and Track B (overlay community 1.23.2 native with symlinks, gate on-device). Validated primary sources and SHA256 checksums. Confirmed sherpa v1.12.34 build-aarch64-linux-gnu.sh is a true x64→arm64 cross-compile (not on-device). Demonstrated cross-compile and QEMU paths work off-device per user directives.

**Status:** Research complete. Merged into Decision 28 with authoritative status over preliminary Hicks proposal.

## Archived Work
- See `history-archive.md` for prior entries (STT semaphore fixes, enhanced-clip pipeline, idle CPU investigation, model app/ audit)

## Next Steps
- Await coordinator approval for PoC hardware allocation (Jetson Orin Nano 8GB)
- Stages 1–5 validation on physical hardware (~3–4 weeks)
