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

### 2026-03-28: /app/models Subdirectory Audit (issue #120)
**Audit Complete — Ready for Hicks to implement**
- All five Wyoming model subdirs are writable at runtime: stt, vad, kws, speech-enhancement, speaker-embedding
- Each subdir has pre-baked models (copied at build time) but also supports runtime model downloads via ModelDownloader + HuggingFaceModelDownloader
- HF CLI uses `--cache-dir /app/models/{subdir}` — no separate `~/.cache/huggingface/` writes to worry about
- Tmpfs config (256MB /tmp) is sufficient for ONNX temp files; no disk writes during inference
- Plugins dir is read-only in voice images (pre-copied at build); can declare as VOLUME for consistency
- Full audit written to `.squad/decisions/inbox/brett-app-models-audit.md`

- **Enhanced clip re-transcription pattern**: Feeding GTCRN per-frame into STT causes buffer discontinuities from overlap-add lag. The fix is to accumulate the full enhanced clip, then re-transcribe in a fresh STT session after VAD end-of-speech. Feature-flagged via `SpeechEnhancementOptions.UseEnhancedClipForStt` (default off). The same flag also gates speaker verification audio source selection — enhanced vs raw.
- **`_utteranceAudioBuffer` vs `_rawUtteranceAudioBuffer`**: When GTCRN enhancement is active, enhanced frames go to `_utteranceAudioBuffer` and raw frames go to `_rawUtteranceAudioBuffer`. Both are plain `List<float>` — no synchronization needed since they're only written during the audio processing loop and read after audio-stop.
- **HybridSttSession re-transcription**: Creating a fresh `ISttSession`, feeding complete audio via `AcceptAudioChunk`, then calling `GetFinalResultAsync` is the correct pattern for single-pass offline transcription of a complete clip. No progressive updates needed.

### 2026-07-23: Idle CPU Investigation (requested by Zack)

**Investigation Complete — No smoking gun found**
- All ONNX/sherpa models are singleton + eagerly loaded, but no idle polling loops exist in voice code
- WyomingServer accept loop has proper 250ms retry delay on failure — not a tight spin
- SSE endpoints are event-driven (BackgroundTaskApi) or 750ms polled (EntityLocationCacheApi) — benign
- Biggest idle CPU suspects are the 5-second config pollers: `SqliteConfigurationProvider` hits DB every tick, `MongoConfigurationProvider` queries Mongo every tick
- `TaskArchivalService` does O(n) task sweep every 5 minutes unconditionally
- Redis/Mongo drivers use default keepalive — no aggressive heartbeats found
- Full findings written to `.squad/decisions/inbox/brett-cpu-investigation.md`

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

### Enhanced Clip A/B Telemetry (requested by Zack)

**Implementation Complete**
- Added 4 OpenTelemetry span tags on `wyoming.process_transcript` activity: `wyoming.stt.audio_source`, `wyoming.stt.enhanced_clip.enabled`, `wyoming.stt.enhanced_clip.sample_count`, `wyoming.stt.enhanced_clip.retranscription_ms`
- Added `enhanced_retranscription` PipelineStageTiming entry alongside existing `stt`, `diarization`, `enhancement` stages
- Added structured Info-level logs distinguishing enhanced vs raw path per utterance
- Fixed `SpanCollectorProcessor` case-sensitive prefix filter (`"Lucia."` → case-insensitive) — Wyoming spans (`lucia.Wyoming.Session`) were silently excluded from the dashboard trace store
- `TranscriptRecord.AudioSource` already existed; now consistently set to `"enhanced_clip"` or `"raw"` matching span tag values
- Build clean, 297/298 Wyoming tests pass (1 pre-existing DI registration failure)

### 2026-05-29: Voice Pipeline Health Review (whole-solution review, requested by Zack)

**Review complete — findings at `review-voice.md` (session files). No code modified.**
- **Unbounded utterance buffers (High):** `_utteranceAudioBuffer`/`_rawUtteranceAudioBuffer` (List<float>) accumulate per chunk and only clear on audio-stop. Per-event payload is capped but cumulative growth is not → OOM risk if a client never sends audio-stop. Needs a max-utterance duration/sample guard.
- **STT semaphore scope (High):** `WyomingServer.RunSessionAsync` holds `_sttConcurrency` (default 4) for the WHOLE session lifetime, not just STT. With `MaxWakeWordStreams=30` accepted, connections beyond 4 sit blocked on WaitAsync after being counted → sockets never read, clients silently hang. Should gate connections separately from STT burst, or acquire the slot only around finalization.
- **Telemetry race (Medium):** fire-and-forget `TrySaveTranscriptRecordAsync` reads `_sttFinalizationMs`/`_diarizationMs`/etc. which `ResetUtteranceAudio()` zeroes right after — stage timings can persist as 0ms. Snapshot timings into locals before Task.Run.
- **EXCLUDE_SPEECH is correctly wired** (Directory.Build.props maps `ExcludeSpeech=true`→`DefineConstants;EXCLUDE_SPEECH`; csproj does `Compile Remove` of Sherpa*/Gtcrn*/WyomingServer/WyomingSession; DI guarded by `#if !EXCLUDE_SPEECH`). BUT no CI job builds with `ExcludeSpeech=true`, so the ARM/Jetson variant can regress unnoticed — recommend adding a build-matrix job.
- **Hostname advertising inconsistency (Medium):** `lucia-{hostname}` is applied only in `WyomingServiceInfo.BuildInfoEvent`; mDNS (`ZeroconfAdvertiser`) still advertises `_options.ServiceName` ("lucia-wyoming"). Discovery name and protocol name diverge.
- **GTCRN per-frame allocations (Medium):** `GtcrnStreamingSession.Process` allocates List + several arrays/tensors per 256-sample hop — sustained GC pressure on the enhancement hot path.
- Confirmed good: parser bounds-checking + ArrayPool + partial-frame handling, writer SemaphoreSlim serialization, idempotent session Dispose, GtcrnStreamingSession does NOT dispose the shared InferenceSession.

- Participated in 2026-05-29 health review
---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.
