# Phase 2d: Granite 4.0 Speech Engine with Keyword Biasing

**Phase**: 2d (extends Phase 2 — STT Quality)
**Priority**: P0 — Transcript Accuracy
**Dependencies**: Phase 2c (GTCRN speech enhancement), enhanced audio pipeline
**Estimated Complexity**: High

## Objective

Replace the sherpa-onnx streaming STT engine with IBM Granite 4.0 1B Speech for dramatically improved transcript accuracy. Granite achieves #1 on the OpenASR leaderboard and supports **keyword list biasing** — allowing us to inject Home Assistant entity names, room names, and command patterns to boost recognition accuracy for home automation vocabulary.

The current sherpa-onnx streaming zipformer produces 50% WER on a simple "Turn on Zack's light in the bedroom, please" command. The target is ≤10% WER, validated by the existing `EnhancementPipeline_WavSample_ProducesTranscript` test.

---

## Why Granite 4.0 1B Speech

| Feature | Benefit for Home Automation |
|---------|---------------------------|
| **#1 OpenASR leaderboard** | Best-in-class accuracy across diverse audio |
| **Keyword biasing** | Boost recognition of entity names, room names, commands |
| **1B parameters** | Small enough for CPU inference on typical home servers |
| **ONNX export** | Direct ONNX Runtime inference, no Python dependency |
| **Streaming (block-attention)** | Real-time partial transcripts with speculative decoding |
| **Multilingual** | English, French, German, Spanish, Portuguese, Japanese |
| **Apache 2.0** | Open source, no license restrictions |

### Keyword Biasing — The Killer Feature

For a home automation assistant, the vocabulary is highly domain-specific. Granite's keyword biasing lets us inject a contextual hotword list:

```
Entity names: "Zack's light", "bedroom lamp", "living room thermostat"
Room names: "kitchen", "bedroom", "garage", "office"  
Commands: "turn on", "turn off", "set temperature", "play music"
People: "Zack", "Sarah", "Mom"
```

This transforms generic speech recognition into **domain-aware** recognition — the model actively favors these words when they're acoustically plausible, dramatically reducing "Zack" → "Colonel Zaxley" type errors.

---

## Architecture

```
Audio Stream
    │
    ▼
┌─────────────┐
│ GTCRN       │ ← Streaming speech enhancement (existing Phase 2c)
│ Enhancement │
└──────┬──────┘
       │ clean audio
       ├───────────────────────────────────────────┐
       ▼                                           ▼
┌──────────────────┐                   ┌───────────────────────┐
│ Sherpa Zipformer  │                   │  Granite 4.0 1B Speech │
│ (streaming)       │                   │  (ONNX Runtime)        │
│ Lightweight       │                   │  + Keyword Bias List   │
│ ~50% WER          │                   │  ≤10% WER target       │
│                   │                   │                        │
│ → Live partials   │                   │ → Final accurate       │
│   for Monitor     │                   │   result for HA        │
└──────────────────┘                   └───────────────────────┘
```

**Dual-engine approach**:
- **Sherpa streaming zipformer**: continues to provide real-time partial transcripts for the Monitor dashboard (fast, low-latency, existing infrastructure)
- **Granite offline**: processes the full enhanced utterance buffer after AudioStop for the **final, accurate transcript** sent back to Home Assistant

This is the hybrid Option 3 approach — live partials for monitoring, accurate finals for action.

---

## Model Details

**Model**: `onnx-community/granite-4.0-1b-speech-ONNX` (HuggingFace)
**Architecture**: 16 conformer blocks with block-attention, CTC + self-conditioned learning
**Size**: ~1-2 GB (ONNX quantized variants available)
**Input**: 16kHz audio features (log-mel spectrogram, 80 bins)
**Output**: Token IDs → decoded via SentencePiece tokenizer
**Keyword bias**: Attention bias on specific tokens during decoding

### ONNX Model Files (from HuggingFace)

```
onnx-community/granite-4.0-1b-speech-ONNX/
├── encoder_model.onnx          # Audio encoder (conformer blocks)
├── decoder_model.onnx          # Token decoder  
├── tokenizer.json              # SentencePiece tokenizer config
├── config.json                 # Model configuration
└── preprocessor_config.json    # Feature extraction config
```

---

## Deliverables

### D1: Granite ONNX Inference Engine

**New files:**

`lucia.Wyoming/Stt/IGraniteEngine.cs`
```csharp
public interface IGraniteEngine
{
    bool IsReady { get; }
    Task<GraniteTranscript> TranscribeAsync(
        float[] enhancedAudio,
        int sampleRate,
        IReadOnlyList<string>? keywordBias = null,
        CancellationToken ct = default);
}
```

`lucia.Wyoming/Stt/GraniteOnnxEngine.cs`
- Loads encoder + decoder ONNX models via `InferenceSession`
- Implements log-mel spectrogram feature extraction (80 bins, 16kHz)
- Token decoding via built-in SentencePiece tokenizer
- Keyword bias injection during beam search / CTC decoding
- Hot-reload via `IModelChangeNotifier` for model switching

`lucia.Wyoming/Stt/GraniteSentencePieceTokenizer.cs`
- Loads `tokenizer.json` from model directory
- Decode token IDs → text
- Map keyword strings → token IDs for biasing

`lucia.Wyoming/Stt/GraniteFeatureExtractor.cs`
- Log-mel spectrogram extraction matching Granite's `preprocessor_config.json`
- 80 mel bins, 25ms window, 10ms hop (standard Whisper-style features)

### D2: Keyword Bias Service

**New file:** `lucia.Wyoming/Stt/KeywordBiasProvider.cs`

Automatically builds the keyword bias list from:
1. **Home Assistant entities**: room names, device names, area names (from `IHomeAssistantClient`)
2. **Command patterns**: existing `CommandPatternRegistry` patterns
3. **Speaker names**: enrolled speaker profiles
4. **Custom keywords**: user-configurable via settings

```csharp
public sealed class KeywordBiasProvider(
    IHomeAssistantClient? haClient,
    CommandPatternRegistry? patternRegistry,
    ISpeakerProfileStore? profileStore)
{
    public async Task<IReadOnlyList<string>> GetKeywordListAsync(CancellationToken ct);
}
```

The keyword list is refreshed periodically (e.g., every 5 minutes) and cached.

### D3: Pipeline Integration

**Modified:** `lucia.Wyoming/Wyoming/WyomingSession.cs`

In `HandleAudioStopEventAsync`, after STT finalization:
```csharp
// Get streaming partial result (fast, for monitor)
var streamingTranscript = _pendingTranscript ?? new SttResult();

// Run Granite on full enhanced buffer (accurate, for HA)
if (_graniteEngine is { IsReady: true } && utteranceAudio.Length > 0)
{
    var keywords = await _keywordBias.GetKeywordListAsync(ct);
    var graniteResult = await _graniteEngine.TranscribeAsync(
        utteranceAudio, _utteranceSampleRate, keywords, ct);
    
    // Use Granite's result as the final transcript
    transcript = new SttResult { Text = graniteResult.Text, Confidence = graniteResult.Confidence };
}
```

### D4: Model Catalog & Dashboard

- Add Granite to `EngineType` or use a new `OfflineStt` category
- Add to `ModelCatalogService` with HuggingFace download URLs
- Dashboard shows Granite engine status alongside streaming STT
- Settings: keyword bias toggle, custom keyword list editor

### D5: Validation

Update the WER gate test:
- `EnhancementPipeline_WavSample_ProducesTranscript` asserts ≤10% WER
- Add Granite-specific tests with keyword biasing enabled
- Multi-sample benchmark across different command types

---

## Task Breakdown

### T1: Model Download & Loading
- [ ] Download `onnx-community/granite-4.0-1b-speech-ONNX` from HuggingFace
- [ ] Implement ONNX model loading (encoder + decoder sessions)
- [ ] Implement tokenizer loading from `tokenizer.json`
- [ ] Add to model catalog with auto-download

### T2: Feature Extraction
- [ ] Implement log-mel spectrogram (80 bins, 16kHz input)
- [ ] Match Granite's `preprocessor_config.json` parameters exactly
- [ ] Unit test: feature shape matches expected dimensions

### T3: Inference Pipeline
- [ ] Implement encoder → decoder inference chain
- [ ] Implement token → text decoding via tokenizer
- [ ] Implement keyword bias injection during decoding
- [ ] Unit test: transcribe known WAV sample

### T4: Keyword Bias Provider
- [ ] Build keyword list from HA entities
- [ ] Build keyword list from command patterns
- [ ] Build keyword list from speaker profiles
- [ ] Caching with periodic refresh
- [ ] Unit test: keyword list generation

### T5: Pipeline Integration
- [ ] Wire Granite engine into WyomingSession
- [ ] Dual-engine: streaming partials + Granite finals
- [ ] Add timing to transcript records
- [ ] Handle graceful fallback if Granite unavailable

### T6: Validation
- [ ] WER gate test passes at ≤10%
- [ ] Keyword biasing improves accuracy for HA entity names
- [ ] Multi-sample benchmark
- [ ] Frontend TypeScript check

---

## Research Needed

1. **Exact ONNX model I/O**: Download the model, inspect tensor shapes and names
2. **Tokenizer format**: Verify `tokenizer.json` can be parsed without Python dependencies
3. **Keyword bias mechanism**: How does Granite inject biasing? Is it attention-level, CTC-level, or beam search-level?
4. **Quantized variants**: Are int8 ONNX exports available for faster CPU inference?
5. **Streaming support**: Can block-attention enable true streaming with Granite, or is it offline only?
