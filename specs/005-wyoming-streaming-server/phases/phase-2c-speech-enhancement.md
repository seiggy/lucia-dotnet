# Phase 2c: GTCRN Streaming Speech Enhancement Pipeline

**Phase**: 2c (extends Phase 2 — Voice Processing Quality)
**Priority**: P0 — Audio Quality Foundation
**Dependencies**: Phase 2b (voice pipeline observability)
**Estimated Complexity**: Medium-High

## Objective

Add **true streaming** speech enhancement as a per-frame pre-processing stage before STT, VAD, and diarization. The StreamGTCRN model processes each 16ms audio frame in ~1-2ms with persistent hidden state (conv cache, TRA cache, inter-RNN cache), cleaning audio before any downstream consumer sees it.

This is the highest-quality approach: STT builds its beam-search hypotheses on clean audio from the very first frame, avoiding the compounding error problem where early misrecognitions bias later word predictions.

Without enhancement, typical satellite microphones capture significant environmental noise that is imperceptible to humans but severely degrades STT accuracy — manifesting as garbled transcripts, missing words, and short-phrase failures.

---

## Architecture

```
Wyoming Client Audio Stream (16kHz, 16-bit mono)
         │
    ┌────┴────┐ (per 256-sample frame, 16ms)
    │         │
    ▼         │
┌─────────────────────┐
│  StreamGTCRN ONNX   │  ← Direct ONNX Runtime inference
│  (per-frame, 16ms)  │     3 cache tensors (conv, TRA, inter-RNN)
│  ~1-2ms latency     │     maintained across frames
└─────────┬───────────┘
          │ clean frame
          ├──────────→ STT (OnlineRecognizer) — streaming partials on clean audio
          ├──────────→ VAD (activity detection on clean audio)
          └──────────→ Utterance buffer (diarization + audio clips)
```

**Key difference from offline approach**: The offline `OfflineSpeechDenoiser` API in sherpa-onnx processes complete audio buffers — it cannot be used per-chunk. The streaming approach uses ONNX Runtime directly (`Microsoft.ML.OnnxRuntime`) to run the StreamGTCRN model frame-by-frame with explicit cache state management.

---

## Streaming Model Details

### StreamGTCRN ONNX Model

The streaming variant of GTCRN exports with these I/O tensors:

**Inputs:**
- `input_frame`: `[1, 1, frame_size]` — single STFT frame (256 samples at 16kHz = 16ms)
- `conv_cache`: `[B, C, T]` — convolutional layer ring buffer state
- `tra_cache`: `[B, C, T]` — temporal recurrent attention state
- `inter_rnn_cache`: `[B, layers, hidden]` — inter-block GRU hidden state

**Outputs:**
- `output_frame`: `[1, 1, frame_size]` — enhanced audio frame
- `conv_cache_out`: updated conv cache
- `tra_cache_out`: updated TRA cache
- `inter_rnn_cache_out`: updated inter-RNN cache

**Frame processing loop:**
```
for each 256-sample frame in audio stream:
    (clean_frame, conv_cache, tra_cache, rnn_cache) = 
        onnx_session.Run(frame, conv_cache, tra_cache, rnn_cache)
    emit clean_frame to STT/VAD/buffer
```

### Model Source

The streaming ONNX model needs to be exported from the official GTCRN repo:
- Source: `https://github.com/Xiaobin-Rong/gtcrn/tree/main/stream`
- Export script: `stream/export_onnx.py` — exports `gtcrn_stream.onnx`
- Reference implementation: `https://github.com/leospark/GTCRN-Online-Stream-Examples`
- Pre-exported models may be available at: `https://github.com/Xiaobin-Rong/gtcrn/tree/main/stream/onnx_models`

If a pre-exported streaming model isn't available from sherpa-onnx releases, we'll need to export it ourselves or host it.

---

## Validation Test Plan

### Test Fixture

**Sample file**: `samples/unfiltered_sample.wav` (16kHz, 16-bit mono, ~2.4s)
**Expected transcript**: `Turn on Zack's light in the bedroom, please.`
**Speaker**: `Unknown` (no enrolled profile)

**Transcript format** (in matching `.txt` file):
```
{SpeakerId}: {transcript text}
```

Multi-speaker samples will use:
```
Unknown 1: First speaker's words
Unknown 2: Second speaker's words
```

### Test Cases

**T-SE-001: Streaming enhancement produces valid audio**
- Load WAV file, split into 256-sample frames
- Process each frame through StreamGTCRN with state
- Verify output has same sample count, valid float range [-1, 1]
- Verify output differs from input (noise was removed)

**T-SE-002: Enhanced audio improves STT accuracy**
- Run STT on raw `unfiltered_sample.wav` → capture transcript
- Run STT on GTCRN-enhanced version → capture transcript
- Compare both against expected text using word error rate (WER)
- Assert enhanced WER < raw WER (or enhanced WER < threshold)
- Run multiple iterations to measure consistency

**T-SE-003: Streaming enhancement matches offline quality**
- Process sample frame-by-frame (streaming) 
- Process same sample as single buffer (offline via `OfflineSpeechDenoiser`)
- Compare outputs — streaming should be within acceptable tolerance of offline

**T-SE-004: Enhancement + full pipeline integration**
- Simulate a Wyoming session: feed WAV file as audio chunks
- Verify streaming partial transcripts appear (via SessionEventBus)
- Verify final transcript matches expected text
- Verify speaker detection fires (Unknown provisional profile created)
- Verify transcript record saved with `enhancement` stage timing

**T-SE-005: Enhancement graceful degradation**
- Disable enhancement (config toggle) → verify STT still works with raw audio
- Corrupt/missing model → verify fallback to raw audio, no crash

**T-SE-006: Multi-run accuracy benchmark**
- Run the same WAV through the full pipeline 10 times
- Collect all transcripts
- Report: mean WER, std dev, best/worst transcript
- This establishes a quality baseline for regression testing

The enhancement stage runs **before** all other processing. Every audio chunk passes through GTCRN first, and the cleaned audio feeds into STT, VAD, and the utterance buffer. This is a single insertion point in `ProcessSpeechSamples`.

---

## Model Selection

### Streaming Model (True Real-Time)

| Model | Size | Frame Size | Latency/Frame | Use Case |
|-------|------|-----------|---------------|----------|
| `gtcrn_stream.onnx` | ~530KB | 256 samples (16ms) | ~1-2ms | **Default** — true streaming, per-frame enhancement |
| `gtcrn_simple_stream.onnx` | ~400KB | 256 samples (16ms) | ~1ms | Ultra-light variant |

### Offline Model (Full-Buffer Fallback)

| Model | Size | RTF (CPU) | Use Case |
|-------|------|-----------|----------|
| `gtcrn_simple.onnx` | ~2MB | 0.07 | Fallback when streaming model unavailable |
| `gtcrn.onnx` | ~4MB | 0.12 | Higher quality offline |

**Model Sources:**
- Streaming: `https://github.com/Xiaobin-Rong/gtcrn/tree/main/stream/onnx_models`
- Offline: `https://github.com/k2-fsa/sherpa-onnx/releases/download/speech-enhancement-models/`

---

## Deliverables

### D1: Streaming Speech Enhancement Engine

**New dependency:** `Microsoft.ML.OnnxRuntime` — for direct ONNX inference with explicit tensor I/O and cache state management. The sherpa-onnx `OfflineSpeechDenoiser` API does not support streaming.

**New files:**

`lucia.Wyoming/Audio/ISpeechEnhancer.cs` *(already exists — update interface)*
```csharp
public interface ISpeechEnhancer
{
    bool IsReady { get; }
    ISpeechEnhancerSession CreateSession();
}
```

`lucia.Wyoming/Audio/ISpeechEnhancerSession.cs` *(new)*
```csharp
public interface ISpeechEnhancerSession : IDisposable
{
    /// <summary>
    /// Enhance a single frame of noisy audio (256 samples at 16kHz = 16ms).
    /// Maintains internal GTCRN cache state across calls for streaming context.
    /// Input frames that don't align to 256 samples are internally buffered.
    /// </summary>
    float[] EnhanceFrame(float[] samples);
}
```

`lucia.Wyoming/Audio/GtcrnStreamingEnhancer.cs`
- Implements `ISpeechEnhancer`
- Loads StreamGTCRN ONNX model via `Microsoft.ML.OnnxRuntime.InferenceSession`
- Subscribes to `IModelChangeNotifier.ActiveModelChanged` for hot-reload
- `CreateSession()` returns a new `GtcrnStreamingSession` with fresh cache state

`lucia.Wyoming/Audio/GtcrnStreamingSession.cs`
- Implements `ISpeechEnhancerSession`
- Holds ONNX Runtime `InferenceSession` reference (shared, read-only) + per-session cache tensors
- **Cache tensors**: `conv_cache`, `tra_cache`, `inter_rnn_cache` — initialized to zeros, updated after each frame
- **Frame buffering**: Accumulates incoming samples until 256-sample frame boundary, then runs inference
- **Output buffering**: Collects enhanced frames and returns them aligned to input chunk boundaries
- `Dispose()` releases cache tensor memory

### D2: Pipeline Integration

**Modified file:** `lucia.Wyoming/Wyoming/WyomingSession.cs`

In `ProcessSpeechSamples`, insert enhancement before STT/VAD/buffer:

```csharp
private ISpeechEnhancerSession? _currentEnhancerSession;

private void ProcessSpeechSamples(ReadOnlySpan<float> samples)
{
    if (_currentSttSession is null) return;

    // Per-frame speech enhancement: clean audio before all downstream processing
    ReadOnlySpan<float> cleanSamples = samples;
    if (_currentEnhancerSession is not null)
    {
        var enhanced = _currentEnhancerSession.EnhanceFrame(samples.ToArray());
        cleanSamples = enhanced;
    }

    AppendUtteranceAudio(cleanSamples, _utteranceSampleRate);
    _currentSttSession.AcceptAudioChunk(cleanSamples, _utteranceSampleRate);

    if (_currentVadSession is not null)
    {
        _currentVadSession.AcceptAudioChunk(cleanSamples);
        TryPublishAudioLevel(cleanSamples, _currentVadSession.HasSpeechSegment);
    }
    else
    {
        TryPublishAudioLevel(cleanSamples, isSpeechActive: true);
    }

    TryPublishPartialTranscript();
}
```

Create enhancer session alongside STT/VAD sessions in `Connected → Transcribing` transition:
```csharp
if (_speechEnhancer is { IsReady: true })
{
    _currentEnhancerSession = _speechEnhancer.CreateSession();
}
```

Dispose in `DisposeEngineSessions`:
```csharp
_currentEnhancerSession?.Dispose();
_currentEnhancerSession = null;
```

### D3: Model Catalog Integration (Already Implemented)

- ✅ `SpeechEnhancement` added to `EngineType` enum
- ✅ GTCRN models in `ModelCatalogService`
- ✅ `SpeechEnhancementOptions` class
- ✅ DI registration, appsettings.json
- ✅ `ModelStartupValidator` bootstraps enhancement engine
- ✅ Dashboard Models tab shows Speech Enhancement
- **TODO**: Update catalog with streaming model URLs once available/exported

### D4: Configuration & Observability

- Add "Speech Enhancement" toggle to Voice Config settings panel
- Add enhancement timing to `TranscriptRecord.Stages` (e.g., `{ Name: "enhancement", DurationMs: 12 }`)
- Status API shows enhancement engine readiness
- Monitor dashboard device cards show enhancement active/inactive

### D5: Dockerfile.voice Update

- Add `gtcrn_simple.onnx` to the model-download stage
- Pre-baked in the `:voice` image alongside other models

---

## Task Breakdown

### T1: Core Engine
- [ ] Add `SpeechEnhancement` to `EngineType` enum
- [ ] Create `ISpeechEnhancer` interface
- [ ] Create `ISpeechEnhancerSession` interface
- [ ] Create `GtcrnSpeechEnhancer` (ONNX model loading, hot-reload)
- [ ] Create `GtcrnEnhancerSession` (streaming inference with state)
- [ ] Add ONNX Runtime dependency if not present

### T2: Catalog & Config
- [ ] Create `SpeechEnhancementOptions` class
- [ ] Add GTCRN models to `ModelCatalogService`
- [ ] Register in DI + configure in appsettings.json
- [ ] Update `ModelStartupValidator` to bootstrap enhancement engine
- [ ] Update `ModelManager.GetModelBasePath` / `GetConfiguredActiveModel` for new engine type

### T3: Pipeline Integration
- [ ] Create enhancer session in `WyomingSession` `Connected → Transcribing` transition
- [ ] Insert enhancement step in `ProcessSpeechSamples`
- [ ] Dispose enhancer session on audio stop / session end
- [ ] Add enhancement timing to transcript records

### T4: Dashboard
- [ ] Add Speech Enhancement section to Models tab (engine type selector)
- [ ] Add enhancement toggle to Voice Config settings
- [ ] Update Status API with enhancement engine readiness
- [ ] Update Dockerfile.voice with GTCRN model download

### T5: Testing
- [ ] Unit tests: GtcrnEnhancerSession (input/output sample count, state persistence)
- [ ] Unit tests: Enhancement bypass when disabled
- [ ] Integration test: Pipeline with enhancement enabled
- [ ] Frontend TypeScript type-check

---

## Notes

- GTCRN expects 16kHz mono input — matches our existing pipeline
- The streaming model maintains hidden states between chunks — session must persist across chunks within a single audio stream
- Frame size (typically 512 samples = 32ms) may not align with incoming chunk sizes — the session needs internal buffering
- Enhancement should be optional and bypassable (config toggle) for debugging
- The enhanced audio should also be used for diarization embeddings and audio clips — cleaner audio = better speaker models
- ONNX Runtime may already be a transitive dependency via SherpaOnnx — check before adding
