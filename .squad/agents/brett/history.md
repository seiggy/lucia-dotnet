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

### IMPLEMENTED off-device native builder + end-to-end validation (2026-07-17T16:51:45-04:00 — Zack)
Authored `infra/docker/Dockerfile.jetson-voice-assets` (my file only) and **actually cross-built it** on
this x64 Windows PC via Docker BuildX (empty context, `--target export`). Durable, empirically-confirmed facts:
- **True x86→aarch64 cross-compile of sherpa v1.12.34 GPU works off-device, no QEMU.** Build host
  `ubuntu:22.04` (pinned digest) + `g++-aarch64-linux-gnu` + upstream `toolchains/aarch64-linux-gnu.toolchain.cmake`.
  sherpa pinned tag v1.12.34 AND asserted commit `12e81142d6fac7182a2cea847a4b7f2170a086a4`.
- **ORT is fetched prebuilt, never compiled.** The exact `onnxruntime-linux-aarch64-gpu-cuda12-1.18.1.tar.bz2`
  is downloaded, `sha256sum -c` verified (`1e91064…`) BEFORE use, and dropped at `/tmp/<name>` — a path
  sherpa's aarch64-gpu cmake treats as a trusted local source and re-hashes via FetchContent URL_HASH.
- **Build only the `sherpa-onnx-c-api` target**; PortAudio/WebSocket/tests/python OFF (drops the alsa-lib
  cross-compile entirely — smaller/faster/more reliable). TTS (espeak/piper) kept ON so the emitted `.so`
  is a drop-in for the NuGet-bundled C API surface.
- **Ubuntu 22.04 build host is deliberate**: (a) glibc 2.35 == JP6.2 userspace; the emitted sherpa `.so`
  requires **max GLIBC_2.34** (verified `readelf -V`) → loads on L4T 36.4.x; a Debian bookworm host
  (glibc 2.36) would have demanded GLIBC_2.36 and broken on-device. (b) Its CMake 3.22 still supports the
  bare `FetchContent_Populate()` sherpa's ORT cmake uses (removed in CMake 3.30+).
- **Validated artifacts (readelf/file):** `libsherpa-onnx-c-api.so` = ELF64 AArch64, stripped, 4,123,208 B,
  SONAME `libsherpa-onnx-c-api.so`, **NEEDED `libonnxruntime.so.1.18.1`** (+libm/libstdc++/libgcc_s/libc/ld);
  no CUDA at link time (providers dlopened at runtime). ORT set: `libonnxruntime.so → .so.1.18.1`
  (SONAME `libonnxruntime.so.1.18.1`, 20,563,104 B), `_providers_cuda.so` 324,966,384 B, `_tensorrt.so`
  839,872 B, `_shared.so` 8,496 B. Export = `FROM scratch` → `/native/` only (no compiler/source/cache).
- **Deploy contract facts for Hicks/Kane:** overlay `/native/*.so*` into `runtimes/linux-arm64/native/`.
  Keep the `libonnxruntime.so → libonnxruntime.so.1.18.1` symlink (managed `Microsoft.ML.OnnxRuntime`
  dlopens `libonnxruntime.so`; sherpa DT_NEEDED targets the versioned soname). This is **Track A** (native
  1.18.1 → managed ORT must be pinned 1.18.1 on linux-arm64). CUDA/cuDNN/TensorRT are NOT bundled — they
  come from the L4T base at runtime.
- **Windows export gotcha:** `--output type=local` fails on NTFS (`A required privilege is not held`) because
  of the ORT symlink; use `--output type=tar,dest=native.tar` (preserves the symlink). Build itself is
  unaffected.

**Status:** Builder implemented + validated off-device. On-device gates (K1 CUDA-EP load, RTF, memory) still
require physical Jetson. No app/package files touched (only my Dockerfile). Coordination note filed to inbox.

### ROOT CAUSE — JetPack 6 CSV mounts ONLY the driver; CUDA runtime must be baked in (2026-07-18T08:37:59-04:00 — Ralph)
Live failure on the Jetson (`zackw@192.168.1.239`, L4T R36.4.7, `Dockerfile.agenthost-jetson-voice`):
`Failed to load library libonnxruntime_providers_cuda.so ... libcublasLt.so.12: cannot open shared object file`.
- **Corrects the prior wrong assumption** ("CUDA/cuDNN/TensorRT ... come from the L4T base at runtime"). On
  JetPack 6 / L4T r36 with `nvidia-container-toolkit` 1.16.2, **CSV mode injects ONLY the CUDA *driver***
  (`libcuda.so.1.1` via `drivers.csv`). `/etc/nvidia-container-runtime/host-files-for-container.d/` had ONLY
  `devices.csv` + `drivers.csv` — **no cuda.csv/cudnn.csv**. This is a change from JP4/5 where CSV mounted
  CUDA/cuDNN. So the CUDA **math/runtime** libs (cuBLAS/cuBLASLt, cudart, cuFFT, cuDNN) are NOT host-provided
  and must ship in-image. Proven read-only via `ldd` inside the deployed container: **5 libs "not found"
  identically with AND without `--runtime nvidia`** — `libcublas/libcublasLt.so.12`, `libcudart.so.12`,
  `libcudnn.so.9`, `libcufft.so.11`. Cross-verified off-device under QEMU (same 5 missing).
- **Fix (Dockerfile-only, minimal):** new `cuda-runtime` stage sources the DT_NEEDED + dlopen closure from the
  **same digest-pinned base ORT was compiled against** (`nvcr.io/nvidia/l4t-jetpack:r36.4.0@sha256:34ccf0…bb673`)
  → ABI/version-exact, matches the host 1:1 (CUDA 12.6.11 / cudart 12.6.68 / cublas 12.6.1.4 / cufft 11.2.6.59 /
  cuDNN 9.3.0), no apt/network/version-guessing. `COPY --from=cuda-runtime /cuda-runtime/lib/ /opt/cuda/lib/`
  into the runtime image + `LD_LIBRARY_PATH=…/native:/opt/cuda/lib`. Overlay ≈1.7 GB (cublasLt 337 MB, cufft
  274 MB, cudnn engines/ops, cublas 124 MB, nvrtc). `libcuda` is NOT baked (correct — host driver via CSV).
- **Validated (complete image `lucia-agenthost-voice:r36.4.7-ort1.23.2-poc-r5`, QEMU, baked ENV):** provider +
  ORT-core + sherpa ldd closures fully resolve (zero "not found"); non-root `appuser` uid 1100; ports 8080/10400;
  entrypoint intact; no python/gcc/nvcc/make/cmake, no .NET SDK dir; 37 model files present. Built by overlaying
  the CUDA runtime onto the already-compiled `-poc-r4` (identical ORT/sherpa layers) to avoid a redundant
  multi-hour QEMU ORT recompile (BuildKit `ort-build` cache was evicted); filesystem-identical to the canonical
  runtime stage. **Still gated by K1:** off-device `ldd` proves load-time symbol resolution ONLY — CUDA-EP
  *registration/kernel execution* on the real GPU still requires physical Jetson with `--runtime nvidia`.

## Archived Work
- See `history-archive.md` for prior entries (STT semaphore fixes, enhanced-clip pipeline, idle CPU investigation, model app/ audit)

## Next Steps
- Await coordinator approval for PoC hardware allocation (Jetson Orin Nano 8GB)
- Stages 1–5 validation on physical hardware (~3–4 weeks)

### 2026-07-18: Jetson Bootstrap Live Deployment

**What I Executed:**
- Copied exact committed `deploy-jetson.sh` to physical Jetson (zackw@192.168.1.239); SHA256 verified
- Executed `--dry-run` bootstrap; passed validation
- Executed real bootstrap; exited 0; all 3 services (AgentHost, Redis, PostgreSQL) deployed and healthy
- Verified: AgentHost `/health` 200, setup wizard redirects, Wyoming 10400 reachable, volumes preserved

**Image Details:**
- Canonical config: `sha256:be790abcba91dc1981f9fc9d2ad149e940d2aa223630cf94e260718ac58291c6`
- Tag: `lucia-voice:latest` (unified Compose project name)

**Gate Status:**
- **Bootstrap complete (B1–B3):** ✓
- **K1 (CUDA-EP):** DEFERRED (requires post-setup strict validation)
- **K2–K5:** Open

**Key Learning:** Three-service unified Compose (AgentHost + Redis + PostgreSQL) bootstraps reliably when CUDA runtime user-space libs are baked into image + `/opt/cuda/lib` overlay.

