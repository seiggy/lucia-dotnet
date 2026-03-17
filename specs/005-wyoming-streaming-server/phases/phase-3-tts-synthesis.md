# Phase 3: Text-to-Speech Synthesis over Wyoming

**Phase**: 3 of 4
**Priority**: P2 — Voice Output
**Dependencies**: Phase 1 (Wyoming Core), Phase 2 (Command Routing — for response text)
**Estimated Complexity**: Medium

## Objective

Add local text-to-speech synthesis to the Wyoming server (running in the `lucia.AgentHost` process) using two ONNX-based TTS engines: Qwen3-TTS (high quality, multi-language) and Chatterbox Turbo (ultra-fast, English). Both engines use direct ONNX Runtime inference only; this is more implementation work, especially around tokenizer ports, but it enables true streaming synthesis and keeps time-to-first-audio under 500ms. Responses from both fast-path commands and LLM orchestration are synthesized into audio and streamed back to Wyoming satellites at native 24kHz by default, with 16kHz fallback when a satellite cannot consume 24kHz.

---

## Architecture

```
Agent Response (text)
        │
        ▼
┌───────────────────────────────┐
│       TTS Engine Selector      │
│                                │
│  Route based on:               │
│  - Configured default engine   │
│  - Per-satellite preference    │
│  - Text language detection     │
│  - Latency requirements        │
│                                │
│  ┌────────┐  ┌──────────────┐ │
│  │ Qwen3  │  │ Chatterbox   │ │
│  │  TTS   │  │   Turbo      │ │
│  │ 0.6B   │  │   350M       │ │
│  └───┬────┘  └──────┬───────┘ │
│      │               │        │
│      └───────┬───────┘        │
│              ▼                │
│       PCM Audio Stream        │
└──────────────┬────────────────┘
               │
               ▼
┌──────────────────────────────┐
│    Wyoming Audio Response     │
│                               │
│  audio-start (24kHz/16bit)   │
│  audio-chunk (streaming)      │
│  audio-chunk ...              │
│  audio-stop                   │
└──────────────────────────────┘
```

---

## Deliverables

### D1: TTS Engine Abstraction

**What**: Common interface for TTS engines with streaming support

**Implementation Details**:

```csharp
public interface ITtsEngine : IDisposable
{
    /// <summary>Engine identifier (e.g., "qwen3-tts", "chatterbox-turbo").</summary>
    string EngineId { get; }
    
    /// <summary>Available voice presets.</summary>
    IReadOnlyList<TtsVoice> AvailableVoices { get; }
    
    /// <summary>Supported languages.</summary>
    IReadOnlyList<string> SupportedLanguages { get; }
    
    /// <summary>Whether this engine is loaded and ready.</summary>
    bool IsReady { get; }
    
    /// <summary>
    /// Synthesize text to audio, yielding chunks as they become available.
    /// </summary>
    IAsyncEnumerable<TtsAudioChunk> SynthesizeStreamingAsync(
        TtsSynthesisRequest request,
        CancellationToken ct);
    
    /// <summary>
    /// Synthesize text to a complete audio buffer.
    /// </summary>
    Task<TtsResult> SynthesizeAsync(
        TtsSynthesisRequest request,
        CancellationToken ct);
}

public sealed record TtsSynthesisRequest
{
    public required string Text { get; init; }
    public string? VoiceName { get; init; }
    public string Language { get; init; } = "english";
    public float Speed { get; init; } = 1.0f;
    public string? ReferenceAudioPath { get; init; }  // For voice cloning
    public string? StyleInstruction { get; init; }     // e.g., "speak with excitement"
}

public sealed record TtsAudioChunk
{
    public required ReadOnlyMemory<byte> PcmData { get; init; }
    public required int SampleRate { get; init; }
    public required int BitsPerSample { get; init; }
    public required int Channels { get; init; }
}

public sealed record TtsVoice
{
    public required string Name { get; init; }
    public required string Language { get; init; }
    public string? Description { get; init; }
}
```

**Files**:
- `lucia.Wyoming/Tts/ITtsEngine.cs`
- `lucia.Wyoming/Tts/TtsSynthesisRequest.cs`
- `lucia.Wyoming/Tts/TtsAudioChunk.cs`
- `lucia.Wyoming/Tts/TtsResult.cs`
- `lucia.Wyoming/Tts/TtsVoice.cs`

---

### D2: Qwen3-TTS Engine

**What**: TTS engine using direct ONNX Runtime inference for Qwen3-TTS

**Implementation Details**:

#### Integration Approach
Qwen3-TTS follows the same direct ONNX pattern as Chatterbox: Lucia loads the tokenizer/front-end and the ONNX sessions directly in C# so PCM frames can be emitted as soon as they are decoded. This is more implementation work than a wrapper package, but it enables true streaming synthesis and keeps time-to-first-audio under 500ms for short phrases.

```csharp
public sealed class QwenTtsEngine : ITtsEngine
{
    private readonly IQwenTokenizer _tokenizer;
    private readonly InferenceSession _textEncoderSession;
    private readonly InferenceSession _acousticDecoderSession;
    private readonly InferenceSession _vocoderSession;
    
    public string EngineId => "qwen3-tts";
    
    public async IAsyncEnumerable<TtsAudioChunk> SynthesizeStreamingAsync(
        TtsSynthesisRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var tokens = _tokenizer.Encode(request.Text, request.Language, request.VoiceName ?? "ryan");
        using var textState = RunTextEncoder(tokens);
        
        await foreach (var audioFrame in DecodeStreamingFramesAsync(textState, request, ct))
        {
            yield return new TtsAudioChunk
            {
                PcmData = ConvertToInt16Pcm(audioFrame),
                SampleRate = 24000,
                BitsPerSample = 16,
                Channels = 1,
            };
        }
    }
}
```

#### Tokenizer / Front-End
- Port the Qwen3-TTS tokenizer and text normalization/front-end to C#
- Load tokenizer vocabulary/config from the model directory alongside the ONNX assets
- Keep the tokenizer in-process so streaming decode does not depend on temporary WAV files or wrapper APIs

#### Voice Presets
Map Wyoming voice selection to Qwen3-TTS voices:
- 9 built-in voices across English, Chinese, Japanese, Korean, Spanish
- Voice cloning supported by generating speaker conditioning from reference audio in-process

#### GPU Acceleration
- Detect CUDA availability at startup
- Fall back to CPU if GPU unavailable
- Configure `InferenceSession` instances with CUDA or DirectML providers where available

**Files**:
- `lucia.Wyoming/Tts/QwenTtsEngine.cs`
- `lucia.Wyoming/Tts/QwenTokenizer.cs`

---

### D3: Chatterbox Turbo Engine

**What**: Ultra-fast TTS using direct ONNX Runtime inference

**Implementation Details**:

#### Integration Strategy
Chatterbox Turbo also runs directly on ONNX Runtime, with the same in-process streaming approach as Qwen3-TTS.

```csharp
public sealed class ChatterboxTtsEngine : ITtsEngine
{
    private readonly InferenceSession _encoderSession;
    private readonly InferenceSession _decoderSession;
    private readonly ITokenizer _tokenizer;
    
    public string EngineId => "chatterbox-turbo";
    
    public async IAsyncEnumerable<TtsAudioChunk> SynthesizeStreamingAsync(
        TtsSynthesisRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var tokens = _tokenizer.Encode(request.Text);
        
        var encoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                new DenseTensor<long>(tokens, new[] { 1, tokens.Length })),
        };
        
        using var encoderOutput = _encoderSession.Run(encoderInputs);
        
        await foreach (var audioFrame in DecodeStreamingFramesAsync(encoderOutput, request, ct))
        {
            yield return new TtsAudioChunk
            {
                PcmData = ConvertToInt16Pcm(audioFrame),
                SampleRate = 24000,
                BitsPerSample = 16,
                Channels = 1,
            };
        }
    }
}
```

#### Tokenizer
- Port the Chatterbox tokenizer to C# or integrate a compatible BPE tokenizer library in-process
- Consider `Microsoft.ML.Tokenizers` if compatible with the model's vocabulary
- Cache tokenized common phrases

#### Implementation Constraint
- No secondary process or IPC fallback is planned; both TTS engines run via direct ONNX Runtime only
- Streaming support is a first-class requirement, not an optional post-processing step

#### Voice Cloning
- Accept 5-10 second reference audio
- Extract speaker conditioning from reference
- Apply during synthesis for voice matching

#### Paralinguistic Tags
- Support `[laugh]`, `[cough]`, `[chuckle]` tags in input text
- Pass through to model as special tokens
- Useful for more natural-sounding responses

**Files**:
- `lucia.Wyoming/Tts/ChatterboxTtsEngine.cs`
- `lucia.Wyoming/Tts/ChatterboxTokenizer.cs`

---

### D4: TTS Engine Selector

**What**: Route synthesis requests to the appropriate TTS engine

**Implementation Details**:

```csharp
public sealed class TtsEngineSelector
{
    private readonly IReadOnlyDictionary<string, ITtsEngine> _engines;
    private readonly TtsConfiguration _config;
    
    public ITtsEngine SelectEngine(TtsSynthesisRequest request, string? satelliteId)
    {
        // 1. Check satellite-specific preference
        if (satelliteId is not null && 
            _config.SatellitePreferences.TryGetValue(satelliteId, out var pref))
            return _engines[pref];
        
        // 2. Check language support
        if (request.Language != "english" && _engines["qwen3-tts"].IsReady)
            return _engines["qwen3-tts"]; // Qwen supports multi-language
        
        // 3. Use fast engine for short text
        if (request.Text.Length < 100 && _engines["chatterbox-turbo"].IsReady)
            return _engines["chatterbox-turbo"];
        
        // 4. Default to configured primary
        return _engines[_config.PrimaryEngine];
    }
}
```

**Files**:
- `lucia.Wyoming/Tts/TtsEngineSelector.cs`

---

### D5: TTS Response Cache

**What**: Cache synthesized audio for frequently spoken phrases

**Implementation Details**:

```csharp
public sealed class TtsCache
{
    private readonly ConcurrentDictionary<string, CachedTtsResponse> _cache;
    private readonly int _maxEntries;
    
    public string ComputeKey(string text, string voice, string language)
        => $"{voice}:{language}:{text.ToLowerInvariant().Trim()}";
    
    public bool TryGet(string key, out CachedTtsResponse? cached);
    public void Set(string key, CachedTtsResponse response);
}
```

Cache candidates (pre-warm at startup):
- "okay"
- "done"
- "lights turned on/off"
- "temperature set to {value}"
- "timer set for {value}"
- "I didn't understand that"
- "sorry, something went wrong"

Cache strategy:
- LRU eviction when max entries reached (default 500)
- Cache key includes voice + language + normalized text
- Invalidate cache when TTS engine or voice changes
- Pre-warm common phrases at startup

**Files**:
- `lucia.Wyoming/Tts/TtsCache.cs`
- `lucia.Wyoming/Tts/CachedTtsResponse.cs`

---

### D6: Wyoming TTS Event Handling

**What**: Handle `synthesize` events and stream TTS audio back

**Implementation Details**:

Update `WyomingSession` to handle TTS:

```csharp
// In WyomingSession, after receiving synthesize event:
private async Task HandleSynthesizeAsync(SynthesizeEvent evt, CancellationToken ct)
{
    var request = new TtsSynthesisRequest
    {
        Text = evt.Text,
        VoiceName = evt.Voice,
        Language = evt.Language ?? "english",
    };
    
    var engine = _ttsSelector.SelectEngine(request, _satelliteId);
    var targetSampleRate = _satelliteCapabilities.Supports24kHzTts ? 24000 : 16000;
    
    // Check cache first
    var cacheKey = _ttsCache.ComputeKey(request.Text, request.VoiceName ?? "default", request.Language);
    if (_ttsCache.TryGet(cacheKey, out var cached) && cached!.SampleRate == targetSampleRate)
    {
        await StreamCachedAudioAsync(cached, ct);
        return;
    }
    
    await _eventWriter.WriteEventAsync(new AudioStartEvent
    {
        Rate = targetSampleRate, Width = 2, Channels = 1
    }, ct);
    
    var chunks = new List<byte>();
    await foreach (var chunk in engine.SynthesizeStreamingAsync(request, ct))
    {
        var wyomingAudio = chunk.SampleRate == targetSampleRate
            ? chunk.PcmData
            : _resampler.Resample(chunk.PcmData, chunk.SampleRate, targetSampleRate);
        
        await _eventWriter.WriteEventAsync(new AudioChunkEvent
        {
            Rate = targetSampleRate, Width = 2, Channels = 1,
            Payload = wyomingAudio
        }, ct);
        
        chunks.AddRange(wyomingAudio.ToArray());
    }
    
    await _eventWriter.WriteEventAsync(new AudioStopEvent(), ct);
    _ttsCache.Set(cacheKey, new CachedTtsResponse { PcmData = chunks.ToArray(), SampleRate = targetSampleRate });
}
```

#### Output Format for Wyoming
- TTS models emit native 24kHz audio and Wyoming should preserve that rate by default
- Satellites that cannot consume 24kHz receive a 16kHz fallback stream
- Resample only when the negotiated satellite capability requires the fallback

**Files**:
- `lucia.Wyoming/Wyoming/WyomingSession.cs` (modify)

---

### D7: Wyoming Info Update

**What**: Advertise TTS capabilities in Wyoming `info` response

**Implementation Details**:
- Include available TTS engines in `info` event
- List available voices per engine
- Include supported languages
- Advertise streaming synthesis capability

```json
{
  "type": "info",
  "data": {
    "asr": [{ "name": "sherpa-zipformer", "languages": ["en"] }],
    "tts": [
      { 
        "name": "qwen3-tts", 
        "languages": ["en", "zh", "ja", "ko", "es"],
        "voices": ["ryan", "emma", "david", "sophia", ...],
        "streaming": true
      },
      {
        "name": "chatterbox-turbo",
        "languages": ["en"],
        "voices": ["default"],
        "streaming": true
      }
    ],
    "wake": [{ "name": "hey-lucia", "languages": ["en"] }]
  }
}
```

**Files**:
- `lucia.Wyoming/Wyoming/WyomingServiceInfo.cs` (modify)

---

### D8: Model Download & Initialization

**What**: Handle TTS model download and first-run initialization

**Implementation Details**:
- Qwen3-TTS and Chatterbox Turbo models are managed as raw ONNX assets by `ModelManager`, including tokenizer/vocabulary files needed for the C# ports
- `ModelManager` in `lucia.Wyoming` is extended for TTS model validation and direct ONNX session warm-up
- Health check reports TTS readiness separately from STT through AgentHost's shared health check system
- Graceful degradation: if TTS models are not present, the Wyoming server (in the AgentHost process) still handles STT/wake
- Streaming readiness checks should verify the tokenizer/front-end assets as well as the ONNX model weights

**Files**:
- `lucia.Wyoming/Models/ModelManager.cs` (modify)
- `lucia.AgentHost/Dockerfile` (update model download / initialization steps)

---

### D9: Tests

**Test Cases**:

#### TTS Engine Tests
- `QwenTtsEngine_SynthesizeText_ReturnsAudioChunks`
- `ChatterboxTtsEngine_SynthesizeText_ReturnsAudio`
- `TtsEngineSelector_ShortEnglishText_SelectsChatterbox`
- `TtsEngineSelector_MultiLanguage_SelectsQwen`
- `TtsEngineSelector_SatellitePreference_Honored`

#### TTS Cache Tests
- `TtsCache_CacheHit_ReturnsStoredAudio`
- `TtsCache_CacheMiss_ReturnsNull`
- `TtsCache_LruEviction_RemovesOldest`
- `TtsCache_DifferentVoice_SeparateCacheEntries`

#### Integration Tests
- `Wyoming_SynthesizeEvent_ReturnsStreamingAudio`
- `Wyoming_SynthesizeWithCache_SecondCallFaster`

**Files**:
- `lucia.Tests/Wyoming/TtsEngineSelectorTests.cs`
- `lucia.Tests/Wyoming/TtsCacheTests.cs`

---

## Task Breakdown

| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P3-TTS-001 | Define ITtsEngine interface | Yes | TTS abstraction + request/response models |
| P3-TTS-002 | Implement QwenTtsEngine | No | Direct ONNX Runtime inference plus Qwen3-TTS tokenizer/front-end port |
| P3-TTS-003 | Implement ChatterboxTtsEngine | No | Direct ONNX Runtime inference with in-process tokenizer |
| P3-TTS-004 | Implement TtsEngineSelector | Yes | Engine routing logic |
| P3-TTS-005 | Implement TtsCache | Yes | LRU cache for synthesized audio |
| P3-TTS-006 | Handle synthesize events | No | Wyoming session TTS event handling with 24kHz default / 16kHz fallback |
| P3-TTS-007 | Update Wyoming info | No | Advertise TTS capabilities |
| P3-TTS-008 | Model management for TTS | No | Download, validation, tokenizer assets, Dockerfile |
| P3-TTS-009 | Add NuGet dependencies | No | Microsoft.ML.OnnxRuntime and any tokenizer support libraries |
| P3-TEST-001 | Write unit tests | Yes | Engine selector, cache tests |
| P3-TEST-002 | Write integration tests | No | Wyoming synthesize flow |

---

## Acceptance Criteria

Phase 3 is complete when:
1. ✅ `synthesize` events produce streaming audio output via Wyoming protocol
2. ✅ Both Qwen3-TTS and Chatterbox Turbo engines work correctly via direct ONNX Runtime inference
3. ✅ Engine selection routes appropriately based on language and latency needs
4. ✅ TTS cache eliminates redundant synthesis for common phrases
5. ✅ Audio is streamed at native 24kHz by default, with 16kHz fallback only for satellites that require it
6. ✅ Wyoming `info` response includes TTS capabilities and available voices
7. ✅ Graceful degradation when TTS models are not installed
8. ✅ All unit and integration tests pass
9. ✅ Time-to-first-audio < 500ms for short phrases on GPU, enabled by direct streaming inference rather than file-based synthesis
