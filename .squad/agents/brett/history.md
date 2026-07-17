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

## Archived Work
- See `history-archive.md` for prior entries (STT semaphore fixes, enhanced-clip pipeline, idle CPU investigation, model app/ audit)

## Next Steps
- Await coordinator approval for PoC hardware allocation (Jetson Orin Nano 8GB)
- Stages 1–5 validation on physical hardware (~3–4 weeks)
