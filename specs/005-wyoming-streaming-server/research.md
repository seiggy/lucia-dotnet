# Technology Research: Wyoming Streaming Voice Server

**Spec**: 005-wyoming-streaming-server
**Date**: 2026-03-13
**Status**: Complete

## 1. Wyoming Protocol

### Overview
The Wyoming protocol is a lightweight, binary-safe, event-based protocol designed for modular voice assistant pipelines. Created by the Rhasspy/OHF-Voice project, it is the standard integration protocol for Home Assistant's local voice stack.

### Protocol Format
- **Transport**: TCP sockets, Unix domain sockets, or stdio
- **Message Format**: Newline-delimited JSON header + optional binary payload
- **Header Fields**:
  - `type` (string): Event type identifier
  - `data` (object): Event-specific metadata
  - `data_length` (int): Bytes of extra UTF-8 JSON data following the header
  - `payload_length` (int): Bytes of binary payload following any extra data

```
{ "type": "audio-chunk", "data": { "rate": 16000, "width": 2, "channels": 1 }, "payload_length": 3200 }\n
<3200 bytes of raw PCM audio>
```

### Core Event Types

| Category | Event | Direction | Description |
|----------|-------|-----------|-------------|
| **Discovery** | `describe` | Client → Server | Request server capabilities |
| **Discovery** | `info` | Server → Client | Respond with available services/models |
| **Audio** | `audio-start` | Bidirectional | Begin audio stream (rate, width, channels) |
| **Audio** | `audio-chunk` | Bidirectional | Raw PCM audio payload |
| **Audio** | `audio-stop` | Bidirectional | End audio stream |
| **STT** | `transcribe` | Client → Server | Request transcription of audio stream |
| **STT** | `transcript` | Server → Client | Return recognized text + confidence |
| **Wake** | `detect` | Client → Server | Begin listening for wake word(s) |
| **Wake** | `detection` | Server → Client | Wake word detected (name, timestamp) |
| **Wake** | `not-detected` | Server → Client | Audio stream ended without detection |
| **TTS** | `synthesize` | Client → Server | Request speech synthesis from text |
| **VAD** | `voice-started` | Server → Client | Speech detected in stream |
| **VAD** | `voice-stopped` | Server → Client | Speech ended in stream |
| **System** | `error` | Server → Client | Protocol or service error |

### Service Discovery
- Wyoming services are discoverable via **Zeroconf/mDNS** (`_wyoming._tcp`)
- Home Assistant auto-discovers Wyoming services on the local network
- Each service type (STT, TTS, wake) can be independently discovered and configured

### Implementation References
- **Specification**: https://github.com/OHF-Voice/wyoming
- **Python reference**: https://pypi.org/project/wyoming/
- **Event reference**: https://julianbei.github.io/wyoming/05-api-reference/event-types/

### Implementation Notes for C#/.NET
No existing C# implementation of the Wyoming protocol exists. We need to build:
1. **TCP listener** using `System.Net.Sockets.TcpListener` or Kestrel raw TCP
2. **Event parser/serializer** for newline-delimited JSON + binary payloads
3. **Session manager** for concurrent client connections
4. **Zeroconf advertiser** using `Makaretu.Dns` or `Zeroconf` NuGet package

---

## 2. sherpa-onnx

### Overview
sherpa-onnx is an open-source, cross-platform speech processing toolkit by k2-fsa that runs entirely offline using ONNX Runtime. It provides streaming ASR, keyword spotting (wake word), VAD, TTS, and speaker diarization — all with official C#/.NET bindings.

### NuGet Package
```xml
<PackageReference Include="org.k2fsa.sherpa.onnx" Version="1.12.0" />
```
- Supports .NET Standard 2.0 and .NET 6.0+
- Pre-built native binaries for Windows, Linux, macOS (x64, ARM64)
- No compilation required

### Streaming Speech Recognition (Online ASR)

#### Architecture
- Supports multiple ASR model families including **Zipformer**, **Paraformer**, **Conformer**, **Whisper**, **SenseVoice**, and NeMo-derived variants
- Streaming-capable models process audio in small chunks (e.g., 10-20ms frames)
- Returns partial results during streaming and final result after completion when using online recognizers
- Users can choose any compatible sherpa-onnx model pack that fits their latency, accuracy, language, and hardware goals

#### C# API
```csharp
// Configuration
var config = new OnlineRecognizerConfig
{
    FeatConfig = new FeatureConfig { SampleRate = 16000, FeatureDim = 80 },
    ModelConfig = new OnlineModelConfig
    {
        Transducer = new OnlineTransducerModelConfig
        {
            Encoder = "encoder.onnx",
            Decoder = "decoder.onnx",
            Joiner = "joiner.onnx",
        },
        Tokens = "tokens.txt",
        NumThreads = 4,
    },
};

// Create recognizer
using var recognizer = new OnlineRecognizer(config);
using var stream = recognizer.CreateStream();

// Feed audio chunks
stream.AcceptWaveform(sampleRate: 16000, samples: audioFloat32);

// Get results
while (recognizer.IsReady(stream))
{
    recognizer.Decode(stream);
}
var result = recognizer.GetResult(stream);
Console.WriteLine(result.Text);
```

#### Supported Model Catalog

Lucia ships with the `sherpa-onnx-streaming-zipformer-en-2023-06-26` model as the default out-of-the-box experience. Users can download and configure any model from the sherpa-onnx ASR model releases at https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models.

Models are categorized by architecture, streaming capability, and language:

##### Streaming Models (Real-Time — Recommended for Wyoming)

| Model | Languages | Size | Architecture | Notes |
|-------|-----------|------|-------------|-------|
| `sherpa-onnx-streaming-zipformer-en-2023-06-26` | English | ~80MB | Zipformer Transducer | **Default** — Best English streaming |
| `sherpa-onnx-streaming-zipformer-en-2023-06-21` | English | ~80MB | Zipformer Transducer | Slightly older English model |
| `sherpa-onnx-streaming-zipformer-en-2023-02-21` | English | ~80MB | Zipformer Transducer | Earlier English model |
| `sherpa-onnx-streaming-zipformer-en-20M-2023-02-17` | English | ~20MB | Zipformer Transducer | Tiny — great for low-resource devices |
| `sherpa-onnx-streaming-zipformer-en-kroko-2025-08-06` | English | ~80MB | Zipformer Transducer | Kroko community English model |
| `sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20` | Chinese+English | ~120MB | Zipformer Transducer | Bilingual |
| `sherpa-onnx-streaming-zipformer-small-bilingual-zh-en-2023-02-16` | Chinese+English | ~60MB | Zipformer Transducer | Small bilingual |
| `sherpa-onnx-streaming-zipformer-multi-zh-hans-2023-12-13` | Multi-Chinese | ~150MB | Zipformer Transducer | Multi-dialect Chinese |
| `sherpa-onnx-streaming-zipformer-zh-2025-06-30` | Chinese | ~100MB | Zipformer Transducer | Latest Chinese (fp16/int8 available) |
| `sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23` | Chinese | ~14MB | Zipformer Transducer | Ultra-small Chinese |
| `sherpa-onnx-streaming-zipformer-fr-2023-04-14` | French | ~80MB | Zipformer Transducer | French |
| `sherpa-onnx-streaming-zipformer-fr-kroko-2025-08-06` | French | ~80MB | Zipformer Transducer | Kroko community French |
| `sherpa-onnx-streaming-zipformer-korean-2024-06-16` | Korean | ~80MB | Zipformer Transducer | Korean |
| `sherpa-onnx-streaming-zipformer-de-kroko-2025-08-06` | German | ~80MB | Zipformer Transducer | Kroko community German |
| `sherpa-onnx-streaming-zipformer-es-kroko-2025-08-06` | Spanish | ~80MB | Zipformer Transducer | Kroko community Spanish |
| `sherpa-onnx-streaming-zipformer-bn-vosk-2026-02-09` | Bengali | ~80MB | Zipformer Transducer | Bengali |
| `sherpa-onnx-streaming-zipformer-small-ru-vosk-2025-08-16` | Russian | ~40MB | Zipformer Transducer | Small Russian (int8 available) |
| `sherpa-onnx-streaming-zipformer-ar_en_id_ja_ru_th_vi_zh-2025-02-10` | 8 languages | ~200MB | Zipformer Transducer | Multilingual (AR/EN/ID/JA/RU/TH/VI/ZH) |
| `sherpa-onnx-streaming-zipformer-ctc-small-2024-03-18` | English | ~40MB | Zipformer CTC | CTC architecture (no transducer) |
| `sherpa-onnx-streaming-paraformer-bilingual-zh-en` | Chinese+English | ~150MB | Paraformer | Streaming Paraformer bilingual |
| `sherpa-onnx-streaming-paraformer-trilingual-zh-cantonese-en` | ZH/Cantonese/EN | ~180MB | Paraformer | Trilingual with Cantonese |
| `sherpa-onnx-streaming-conformer-en-2023-05-09` | English | ~100MB | Conformer | Streaming Conformer |
| `sherpa-onnx-streaming-conformer-zh-2023-05-23` | Chinese | ~100MB | Conformer | Streaming Conformer |
| `sherpa-onnx-nemo-streaming-fast-conformer-ctc-en-80ms` | English | ~100MB | NeMo FastConformer | 80ms latency (int8 available) |
| `sherpa-onnx-nemo-streaming-fast-conformer-ctc-en-480ms` | English | ~100MB | NeMo FastConformer | 480ms latency (int8 available) |
| `sherpa-onnx-nemo-streaming-fast-conformer-transducer-en-80ms` | English | ~120MB | NeMo FastConformer | Transducer 80ms (int8 available) |
| `sherpa-onnx-nemotron-speech-streaming-en-0.6b-int8-2026-01-14` | English | ~600MB | Nemotron | Large, high-accuracy streaming |

##### Offline Models (Batch — Higher Accuracy, Use for Short Utterances)

| Model | Languages | Size | Architecture | Notes |
|-------|-----------|------|-------------|-------|
| `sherpa-onnx-whisper-tiny.en` | English | ~75MB | Whisper | Fastest Whisper |
| `sherpa-onnx-whisper-base.en` | English | ~140MB | Whisper | Good balance |
| `sherpa-onnx-whisper-small.en` | English | ~460MB | Whisper | Accurate |
| `sherpa-onnx-whisper-medium.en` | English | ~1.5GB | Whisper | Very accurate |
| `sherpa-onnx-whisper-tiny` | Multilingual | ~75MB | Whisper | 99 languages |
| `sherpa-onnx-whisper-small` | Multilingual | ~460MB | Whisper | 99 languages |
| `sherpa-onnx-whisper-medium` | Multilingual | ~1.5GB | Whisper | 99 languages |
| `sherpa-onnx-whisper-large-v3` | Multilingual | ~3GB | Whisper | Highest accuracy |
| `sherpa-onnx-whisper-turbo` | Multilingual | ~800MB | Whisper | Optimized large model |
| `sherpa-onnx-whisper-distil-large-v3.5` | Multilingual | ~1.5GB | Whisper | Distilled for speed |
| `sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2025-09-09` | ZH/EN/JA/KO/Cantonese | ~200MB | SenseVoice | 5-language (int8 available) |
| `sherpa-onnx-zipformer-en-2023-06-26` | English | ~80MB | Zipformer | Offline Zipformer |
| `sherpa-onnx-zipformer-en-libriheavy-20230926-large` | English | ~350MB | Zipformer | Large, trained on LibriHeavy |
| `sherpa-onnx-zipformer-en-libriheavy-20230830-large-punct-case` | English | ~350MB | Zipformer | With punctuation + casing |
| `sherpa-onnx-paraformer-zh-2025-10-07` | Chinese | ~220MB | Paraformer | Latest Chinese (int8 available) |
| `sherpa-onnx-paraformer-trilingual-zh-cantonese-en` | ZH/Cantonese/EN | ~250MB | Paraformer | Trilingual |
| `sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8` | English | ~300MB | NeMo Parakeet | High accuracy, quantized |
| `sherpa-onnx-nemo-canary-180m-flash-en-es-de-fr` | EN/ES/DE/FR | ~180MB | NeMo Canary | 4-language (int8 available) |
| `sherpa-onnx-zipformer-korean-2024-06-24` | Korean | ~80MB | Zipformer | Offline Korean |
| `sherpa-onnx-zipformer-thai-2024-06-20` | Thai | ~80MB | Zipformer | Offline Thai |
| `sherpa-onnx-zipformer-ru-2025-04-20` | Russian | ~80MB | Zipformer | Russian (int8 available) |
| `sherpa-onnx-zipformer-vi-2025-04-20` | Vietnamese | ~80MB | Zipformer | Vietnamese (int8 available) |
| `sherpa-onnx-zipformer-ja-reazonspeech-2024-08-01` | Japanese | ~80MB | Zipformer | Japanese |

##### Model Selection Guidance

| Priority | Recommendation |
|----------|---------------|
| **Lowest latency** | Streaming Zipformer (default) — processes audio in real-time as it arrives |
| **Best accuracy (English)** | Whisper large-v3 or Nemotron 0.6B (offline, batch processing after audio-stop) |
| **Multilingual** | Streaming: `ar_en_id_ja_ru_th_vi_zh` 8-language model; Offline: Whisper or SenseVoice |
| **Low resource / RPi** | `streaming-zipformer-en-20M` (20MB) or `streaming-zipformer-zh-14M` (14MB) |
| **Punctuation + casing** | `zipformer-en-libriheavy-punct-case` variants (offline only) |
| **Quantized (faster CPU)** | Look for `-int8` or `-fp16` variants of any model |

All models are downloaded from: `https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/<model-name>.tar.bz2`

### Keyword Spotting (Wake Word)

#### Architecture
- Dedicated keyword spotter model separate from ASR
- Processes continuous audio stream
- Detects custom keywords/phrases with configurable sensitivity
- Low CPU usage suitable for always-on operation

#### C# API
```csharp
var config = new KeywordSpotterConfig
{
    FeatConfig = new FeatureConfig { SampleRate = 16000, FeatureDim = 80 },
    ModelConfig = new OnlineModelConfig
    {
        Transducer = new OnlineTransducerModelConfig
        {
            Encoder = "encoder.onnx",
            Decoder = "decoder.onnx",
            Joiner = "joiner.onnx",
        },
        Tokens = "tokens.txt",
    },
    KeywordsFile = "keywords.txt",  // One keyword per line
    NumTrailingBlanks = 1,
};

using var spotter = new KeywordSpotter(config);
using var stream = spotter.CreateStream();

// Feed audio continuously
stream.AcceptWaveform(sampleRate: 16000, samples: audioChunk);

while (spotter.IsReady(stream))
{
    spotter.Decode(stream);
}

var result = spotter.GetResult(stream);
if (!string.IsNullOrEmpty(result.Keyword))
{
    Console.WriteLine($"Detected: {result.Keyword}");
}
```

#### Open-Vocabulary Architecture (Zero-Shot Wake Words)

Unlike traditional wake word systems (Porcupine, Snowboy) that require model training per keyword, sherpa-onnx uses **open-vocabulary keyword spotting**. This is a critical architectural advantage:

1. **How it works**: The KWS model is actually a small streaming ASR (transducer) model. During decoding, the beam search is constrained to only accept token sequences matching the configured keywords — everything else is rejected.
2. **Zero training required**: Any text phrase can become a wake word by tokenizing it into the model's vocabulary. No audio samples needed for basic functionality.
3. **Runtime keyword changes**: Keywords can be added, removed, or modified at runtime without restarting the model. The `KeywordSpotter` supports per-stream keywords via `CreateStream(keywordsString)`.

#### text2token Conversion

Custom wake phrases must be converted to the model's token format:

```
# Input: "hey jarvis"
# Output (BPE tokens): ▁HE Y ▁J AR V IS

# Input: "computer"  
# Output: ▁CO M PU T ER
```

sherpa-onnx provides a `text2token` utility. In our C# implementation, we'll port the tokenization logic to run in-process using the model's `tokens.txt` vocabulary file.

#### keywords.txt Format

```
# Format: <tokens> :<boost_score> #<threshold>
▁HE Y ▁LU C I A :1.5 #0.35
▁HE Y ▁J AR V IS :1.2 #0.30
▁CO M PU T ER :1.0 #0.40
```

- **Boost score** (`:float`): Increases the keyword's weight in beam search. Higher = more sensitive. Default: 1.0
- **Threshold** (`#float`): Minimum confidence to trigger detection. Lower = more sensitive. Default: 0.25
- Multiple keywords supported simultaneously (one per line)

#### Calibration Strategy

While zero samples are needed for basic wake word functionality, optional calibration (3-5 spoken samples) significantly improves detection quality:

1. User says the wake word 3-5 times via browser microphone
2. System runs each sample through the keyword spotter
3. Measures detection confidence for each sample
4. Auto-tunes boost and threshold:
   - If all samples detected with high confidence (> 0.6): lower boost, raise threshold (reduce false positives)
   - If some samples missed: raise boost, lower threshold (improve recall)
   - Target: ≥ 95% recall with < 1 false positive per hour
5. Environmental noise profile captured during calibration pauses (used to set noise floor)

This approach gives us the best of both worlds: zero-friction default setup with optional precision tuning.

### Voice Activity Detection (VAD)

#### Purpose
- Segments audio into speech/non-speech regions
- Reduces unnecessary STT processing
- Improves transcription accuracy by removing silence

#### C# API
```csharp
var vadConfig = new VadModelConfig
{
    SileroVad = new SileroVadModelConfig
    {
        Model = "silero_vad.onnx",
        Threshold = 0.5f,
        MinSpeechDuration = 0.25f,
        MinSilenceDuration = 0.5f,
    },
    SampleRate = 16000,
};

using var vad = new VoiceActivityDetector(vadConfig, bufferSizeInSeconds: 30);
vad.AcceptWaveform(samples);

while (!vad.IsEmpty())
{
    var segment = vad.Front();
    // Process speech segment
    vad.Pop();
}
```

### Speaker Diarization

#### Architecture
sherpa-onnx provides offline speaker diarization using:
1. **Speaker segmentation model** (pyannote-based ONNX export)
2. **Speaker embedding model** (for clustering segments by speaker)
3. **Agglomerative clustering** for speaker assignment

#### C# API
```csharp
var config = new OfflineSpeakerDiarizationConfig
{
    Segmentation = new OfflineSpeakerSegmentationModelConfig
    {
        Pyannote = new OfflineSpeakerSegmentationPyannoteModelConfig
        {
            Model = "sherpa-onnx-pyannote-segmentation-3-0.onnx",
        },
    },
    Embedding = new SpeakerEmbeddingExtractorConfig
    {
        Model = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx",
    },
    Clustering = new FastClusteringConfig
    {
        NumClusters = -1,  // Auto-detect
        Threshold = 0.5f,
    },
    MinDurationOn = 0.3f,
    MinDurationOff = 0.5f,
};

using var diarizer = new OfflineSpeakerDiarization(config);
var segments = diarizer.Process(samples);
foreach (var segment in segments)
{
    Console.WriteLine($"Speaker {segment.Speaker}: {segment.Start:F2}s - {segment.End:F2}s");
}
```

#### Streaming Adaptation
The offline API processes complete audio. For real-time streaming:
- Process overlapping windows (e.g., 10-second windows with 5-second overlap)
- Maintain speaker embedding cache for cross-window consistency
- Use online clustering to assign speakers incrementally

---

## 3. Qwen3-TTS (Direct ONNX Runtime)

### Overview
Qwen3-TTS is Alibaba's open-source text-to-speech model, available in ONNX format on HuggingFace. Lucia uses **direct ONNX Runtime inference** (not the ElBruno.QwenTTS NuGet wrapper) to enable true streaming synthesis and meet the <500ms TTFA target. This requires porting the Qwen3-TTS tokenizer/front-end pipeline to C#.

### Dependencies
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" />  <!-- Pinned in Directory.Packages.props -->
```

### Capabilities
- **Model sizes**: 0.6B (4-6GB VRAM) and 1.7B (6-8GB VRAM)
- **Output**: 24kHz mono WAV
- **Languages**: English, Spanish, Chinese, Japanese, Korean (9 preset voices)
- **Voice cloning**: From 3-second reference audio sample
- **Instruction control**: "speak with excitement", "whisper", etc.
- **GPU acceleration**: CUDA and DirectML supported
- **Auto-download**: Models auto-download on first use (~5.5GB for 0.6B)

### C# Usage (Direct ONNX Runtime)
```csharp
using Microsoft.ML.OnnxRuntime;

// Load Qwen3-TTS ONNX model
var session = new InferenceSession("qwen3-tts-0.6b/model.onnx");

// Tokenize input (ported from Python front-end)
var tokens = QwenTokenizer.Encode(text, voice, language);

// Run inference — yields audio frames for streaming
var inputs = new List<NamedOnnxValue>
{
    NamedOnnxValue.CreateFromTensor("input_ids", tokens),
    // ... voice conditioning, language embedding
};

using var results = session.Run(inputs);
var audioOutput = results.First().AsEnumerable<float>().ToArray();
// Stream as 24kHz PCM chunks
```

### Performance Characteristics
- **Time-to-first-audio**: ~1-2 seconds on GPU, 3-5 seconds on CPU (0.6B model)
- **Real-time factor**: ~0.3x on modern GPU (generates audio 3x faster than real-time)
- **Memory**: ~4-6GB for 0.6B model
- **Streaming**: The current NuGet API is file-based; streaming will require custom pipeline integration

### Integration Notes
- The `TtsPipeline` API writes to files; we'll need to wrap it to produce streaming PCM chunks
- Model download should be handled during container build or first-run initialization
- Voice preset selection maps naturally to Wyoming's voice selection in `synthesize` events

---

## 4. Chatterbox Turbo TTS (Resemble AI)

### Overview
Chatterbox Turbo is Resemble AI's open-source TTS model optimized for speed. The 350M parameter model achieves sub-150ms inference with high-quality, expressive output. Available in ONNX format on Hugging Face.

### Availability
- **Model**: `ResembleAI/chatterbox-turbo-ONNX` on Hugging Face
- **License**: MIT (free for commercial use)
- **Primary language**: English (optimized for maximum performance)
- **Current ecosystem**: Python-first (`chatterbox-tts` PyPI), ONNX export available

### Capabilities
- **Paralinguistic tags**: `[laugh]`, `[cough]`, `[chuckle]` in input text
- **Zero-shot voice cloning**: 5-10 seconds of reference audio
- **Emotion/exaggeration control**: Single parameter for expressiveness
- **Watermarking**: Optional PerTh watermarking for provenance
- **Speed**: Sub-150ms inference, up to 6x real-time on GPU

### Integration Strategy for C#/.NET
Unlike Qwen3-TTS, Chatterbox doesn't have a ready-made C# NuGet wrapper. Options:

1. **Direct ONNX Runtime inference** (Recommended):
   ```csharp
   using Microsoft.ML.OnnxRuntime;
   
   // Load model
   var session = new InferenceSession("chatterbox-turbo.onnx");
   
   // Tokenize input text (need to port tokenizer or use SentencePiece)
   var inputIds = Tokenize(text);
   
   // Run inference
   var inputs = new List<NamedOnnxValue>
   {
       NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
       // ... additional inputs per model architecture
   };
   
   using var results = session.Run(inputs);
   var audioOutput = results.First().AsEnumerable<float>().ToArray();
   ```

2. **Python sidecar process**: Run Chatterbox in a Python subprocess with IPC
3. **gRPC wrapper**: Wrap the Python Chatterbox in a gRPC service

**Recommendation**: Start with option 1 (direct ONNX) for maximum performance and simplicity. If tokenizer porting proves complex, fall back to option 2 with a Python sidecar managed by Aspire.

### Performance Characteristics
- **Time-to-first-audio**: < 150ms on GPU
- **Model size**: ~350M parameters, ~1.5GB on disk
- **Memory**: ~2-3GB VRAM
- **Quality**: Comparable to ElevenLabs in user studies

---

## 5. Speaker Diarization for Command Routing

### Approach: Hybrid Diarization + Intent Classification

The diarization engine serves two purposes:
1. **Speaker identification** — who is speaking (for authorization and personalization)
2. **Command shortcutting** — fast-path known command patterns to skip LLM

### Architecture

```
Audio Stream
    │
    ▼
┌──────────┐     ┌───────────────┐
│   VAD    │────▶│ STT (sherpa)  │
└──────────┘     └───────┬───────┘
                         │ transcript
                         ▼
              ┌──────────────────────┐
              │  Speaker Embedding   │
              │  (sherpa-onnx)       │
              └──────────┬───────────┘
                         │ speaker_id + transcript
                         ▼
              ┌──────────────────────┐
              │  Command Pattern     │
              │  Matcher             │
              └──────────┬───────────┘
                    ┌────┴────┐
                    │         │
              high conf   low conf
                    │         │
                    ▼         ▼
              ┌─────────┐ ┌──────────────┐
              │ Direct   │ │ LLM          │
              │ Skill    │ │ Orchestrator │
              │ Dispatch │ │ (LuciaEngine)│
              └─────────┘ └──────────────┘
```

### Command Pattern Matching Strategy

Rather than using an SLM/LLM for classification, use a deterministic approach:

1. **Template registry**: Register patterns per skill
   ```
   LightControlSkill:
     - "turn {on|off} [the] {entity}"
     - "{entity} {on|off}"
     - "lights {on|off} [in] {area}"
   
   ClimateControlSkill:
     - "set {entity|area} [temperature] to {value} [degrees]"
     - "make it {warmer|cooler|hotter|colder} [in] {area}"
   
   SceneControlSkill:
     - "activate {scene}"
     - "set [the] scene [to] {scene}"
   ```

2. **Entity resolution**: Use existing `IEntityLocationService.SearchHierarchyAsync()` to resolve entity names from the transcript

3. **Confidence scoring**: Combine template match score with entity resolution confidence

4. **Fuzzy matching**: Use `StringSimilarity` (already in codebase) for STT artifact tolerance

### Speaker Embedding for Authorization

- Enroll speaker profiles during setup (record 10-second sample per person)
- Extract embeddings using sherpa-onnx's speaker embedding model
- Compare real-time embeddings with enrolled profiles using cosine similarity
- Threshold-based identification (configurable; default > 0.7 cosine similarity)

---

## 6. Audio Processing Considerations

### Audio Format Pipeline
```
Satellite Mic → 16kHz 16-bit mono PCM → Wyoming chunks → Server
Server → VAD → Speech segments → STT
Server → TTS → 24kHz 16-bit mono PCM → Wyoming chunks → Satellite Speaker
```

### Resampling
- sherpa-onnx expects 16kHz input
- Qwen3-TTS outputs 24kHz
- Chatterbox Turbo outputs at model-specific rate
- Need resampling utility (use `NAudio` or manual linear interpolation for simple cases)

### Buffer Management
- Audio chunks arrive in 10-20ms frames (~320-640 bytes at 16kHz/16-bit)
- Need ring buffer for VAD look-ahead
- STT processes frames as they arrive (streaming)
- Wake word detector runs continuously on same audio stream

### Concurrency Model
- Each Wyoming TCP connection gets its own processing pipeline
- sherpa-onnx supports thread-safe operation with separate stream instances
- TTS requests can be queued (single model instance, sequential synthesis)
- Use `System.Threading.Channels` for audio chunk pipeline

---

## 7. Deployment & Model Management

### Model Storage
- Models should be stored in a configurable directory (default: `/models/`)
- Aspire can mount a shared volume for model files
- Docker images can pre-bake models or download on first run

### Recommended Model Set (Phase 1)
| Purpose | Model | Size | Source |
|---------|-------|------|--------|
| Streaming ASR | `sherpa-onnx-streaming-zipformer-en-2023-06-26` | ~80MB | k2-fsa releases |
| Wake Word | `sherpa-onnx-kws-zipformer-*` | ~40MB | k2-fsa releases |
| VAD | `silero_vad.onnx` | ~2MB | sherpa-onnx bundled |
| Speaker Embedding | `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx` | ~90MB | k2-fsa releases |

### Recommended Model Set (Phase 3 — TTS)
| Purpose | Model | Size | Source |
|---------|-------|------|--------|
| TTS (Primary) | Qwen3-TTS 0.6B ONNX | ~5.5GB | HuggingFace |
| TTS (Fast) | Chatterbox Turbo ONNX | ~1.5GB | HuggingFace (ResembleAI) |

### Hardware Requirements
| Component | CPU-Only | With GPU |
|-----------|----------|----------|
| Minimum RAM | 4GB | 4GB |
| Minimum VRAM | N/A | 6GB (for TTS) |
| Recommended CPU | 4-core x86_64 | Any |
| Disk (models) | ~8GB (all models) | Same |

---

## 8. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| sherpa-onnx C# API instability | Low | High | Pin NuGet version; wrap in abstraction layer |
| Chatterbox Turbo lacks C# wrapper | High | Medium | Direct ONNX Runtime inference; Python sidecar fallback |
| Qwen3-TTS model size (5.5GB) | Medium | Medium | Support model selection; Chatterbox as lighter alternative |
| Wake word false positives | Medium | Medium | Configurable sensitivity; VAD pre-filter; user testing |
| Streaming diarization accuracy | Medium | Low | Start with offline per-utterance; optimize to streaming later |
| Wyoming protocol changes | Low | Low | Abstract protocol layer; follow OHF-Voice releases |
| Multi-satellite audio routing | Low | Medium | Independent sessions by design; test with 4+ concurrent connections |
