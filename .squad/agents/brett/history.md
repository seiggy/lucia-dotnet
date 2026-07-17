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

## Archived Work
- See `history-archive.md` for prior entries (STT semaphore fixes, enhanced-clip pipeline, idle CPU investigation, model app/ audit)

## Next Steps
- Await coordinator approval for PoC hardware allocation (Jetson Orin Nano 8GB)
- Stages 1–5 validation on physical hardware (~3–4 weeks)
