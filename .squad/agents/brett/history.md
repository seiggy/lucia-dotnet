# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, sherpa-onnx 1.12.29 (bundles ORT 1.23.2), Wyoming protocol, ONNX Runtime
- **Created:** 2026-03-26

## Key Systems I Own

- `lucia.Wyoming/` — Full voice pipeline
- STT Engines: HybridSttEngine, SherpaSttEngine, GraniteOnnxEngine
- Granite 4.0 1B Speech ONNX: 3-model pipeline (audio_encoder, embed_tokens, decoder_model_merged)
- Wake word: configurable detection
- VAD: Voice Activity Detection
- Diarization: speaker verification profiles
- Model management: download, catalog, safety validation (reject "..", path separators)
- Wyoming TCP server: IHostedService, concurrency-limited sessions
- Zeroconf/mDNS: service discovery for HA

## Key Constraints

- SherpaOnnx.DenoisedAudio has Dispose() but does NOT implement IDisposable — use explicit try/finally
- Model IDs must be filesystem-safe (no "..", no path separators)
- Audio pipeline latency is critical — measure at every stage
- All processing local — no cloud dependencies for voice

## Learnings

<!-- Append new learnings below. -->

- **Enhanced clip re-transcription pattern**: Feeding GTCRN per-frame into STT causes buffer discontinuities from overlap-add lag. The fix is to accumulate the full enhanced clip, then re-transcribe in a fresh STT session after VAD end-of-speech. Feature-flagged via `SpeechEnhancementOptions.UseEnhancedClipForStt` (default off). The same flag also gates speaker verification audio source selection — enhanced vs raw.
- **`_utteranceAudioBuffer` vs `_rawUtteranceAudioBuffer`**: When GTCRN enhancement is active, enhanced frames go to `_utteranceAudioBuffer` and raw frames go to `_rawUtteranceAudioBuffer`. Both are plain `List<float>` — no synchronization needed since they're only written during the audio processing loop and read after audio-stop.
- **HybridSttSession re-transcription**: Creating a fresh `ISttSession`, feeding complete audio via `AcceptAudioChunk`, then calling `GetFinalResultAsync` is the correct pattern for single-pass offline transcription of a complete clip. No progressive updates needed.

### 2026-04-14: Enhanced Clip STT Pipeline Implementation (w/ Lambert QA)

**Implementation Complete**
- Feature flag `UseEnhancedClipForStt` added to SpeechEnhancementOptions
- Re-transcription path integrated in ProcessTranscriptAsync with proper guard conditions
- Enhanced audio routing to speaker verification (gates on same flag)
- Build clean, 288 tests pass (9 new integration tests from Lambert)
- Orchestration logs: `.squad/orchestration-log/2026-04-14T20-45-02Z-brett.md`

**Key Integration Points**
- Config: `Wyoming:Models:SpeechEnhancement:UseEnhancedClipForStt` (boolean, hot-reloadable)
- When flag OFF: raw audio path unchanged, enhancement only for clip storage
- When flag ON: post-VAD re-transcription adds ~1 inference pass; timing logged at Info level
- Speaker verification: routes enhanced utterance when flag ON, raw when flag OFF

**Decisions Merged**
- Decision #9: Feature-flagged Enhanced Clip STT Pipeline (status: Implemented, flag OFF by default)
- Decision #10: Enhanced Clip Pipeline Test Strategy (status: Implemented, 9 tests all green)
