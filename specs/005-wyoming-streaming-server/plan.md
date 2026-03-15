# Implementation Plan: Wyoming Streaming Voice Server

**Branch**: `feat-wyoming-server`
**Date**: 2026-03-13
**Spec**: `specs/005-wyoming-streaming-server/spec.md`
**Input**: Full Wyoming protocol support for streaming STT, TTS, wake word, diarization with command routing

## Summary

Build a new `lucia.Wyoming` class library that implements the Wyoming voice protocol and runs in-process inside `lucia.AgentHost`, enabling Lucia to directly handle streaming audio from Home Assistant satellites. Rather than introducing a separate host, the Wyoming TCP listener and voice pipeline become part of the existing agent runtime.

This architecture is simpler for users to deploy and operate: one process, one container, and one config file. Phase 1 bridges audio turns into Lucia's existing text-based runtime, while Phase 2 retools the orchestrator contract for metadata-rich voice-native invocation.

The implementation is divided into phases:
1. **Phase 1**: Wyoming protocol TCP server + device pairing + sherpa-onnx streaming STT + wake word detection
2. **Phase 2**: Speaker verification engine with command pattern matching and metadata-rich LLM fallback routing
3. **Phase 2b**: Advanced voice profiling (auto-profile audio clips, profile merging), diarization configuration UI, and real-time session monitoring dashboard
4. **Phase 2c**: GTCRN streaming speech enhancement — real-time noise removal before STT/VAD/diarization
5. **Phase 2d**: Granite 4.0 1B Speech engine with keyword biasing — high-accuracy STT with HA entity-aware vocabulary
6. **Phase 3**: Local TTS synthesis via Qwen3-TTS and Chatterbox Turbo over Wyoming using direct ONNX Runtime
7. **Phase 4**: Full end-to-end pipeline integration, multi-satellite support, and performance optimization

## Technical Context

### Current Architecture
- **Agent runtime**: Phase 1 bridges Wyoming turns into today's text-in/text-out `LuciaEngine` orchestrator; Phase 2 extends that contract for metadata-rich voice-native invocation
- **Deployment**: `lucia.AppHost` (Aspire) orchestrates `lucia.AgentHost` + `lucia.A2AHost` + dashboard
- **Voice today**: HA conversation agent integration (text only); HA handles all audio via its own voice pipeline
- **Existing voice-adjacent code**: `continue_conversation` semantics, `assist_satellite.announce` for TTS via HA, alarm media playback

### Integration Strategy
The Wyoming server is a **new in-process edge transport layer**, not a separate host and not an internal agent. It runs inside `lucia.AgentHost`, exposing a Wyoming TCP listener alongside the existing HTTP API while connecting directly to Lucia's runtime services:

```
Wyoming Satellite ←──TCP──→ lucia.AgentHost (Wyoming TCP listener + HTTP API)
                              │
                         Both Audio + Text Domain
                    (STT, TTS, Wake, VAD, Agents, Skills, LLM)
```

### Why In-Process
- Single deployment artifact — one container, one config file
- Zero-latency command dispatch — direct in-process calls to `LuciaEngine` and skills
- Direct DI dispatch only — no mesh mode, sidecar, or loopback HTTP fallback
- Simpler for users — no service discovery between Wyoming and AgentHost
- Shared service infrastructure — Redis, MongoDB, telemetry, and DI registrations are reused
- Wyoming TCP port exposed alongside HTTP API (`10400` + `8080`)

## Constitution Check

- ✅ **One Class Per File**: All new classes will follow this rule
- ✅ **Test-First Development**: Tests defined per phase before implementation
- ✅ **Documentation-First Research**: Research document completed (research.md)
- ✅ **Privacy-First Architecture**: All processing local, no cloud APIs
- ✅ **Observability & Telemetry**: OpenTelemetry spans/metrics for every pipeline stage

## Project Structure

### New Projects

```
lucia.Wyoming/                  # New class library for Wyoming protocol + voice
├── Wyoming/                    # Wyoming protocol implementation
│   ├── WyomingServer.cs        # TCP listener (IHostedService)
│   ├── WyomingSession.cs
│   ├── WyomingEventParser.cs
│   ├── WyomingEventWriter.cs
│   ├── WyomingEvent.cs
│   └── WyomingServiceInfo.cs
├── Audio/
│   ├── AudioPipeline.cs
│   ├── AudioResampler.cs
│   ├── AudioBuffer.cs
│   └── AudioFormat.cs
├── Stt/
│   ├── ISttEngine.cs
│   ├── SherpaSttEngine.cs
│   └── SttResult.cs
├── WakeWord/
│   ├── IWakeWordDetector.cs
│   ├── SherpaWakeWordDetector.cs
│   └── WakeWordResult.cs
├── Vad/
│   ├── IVadEngine.cs
│   └── SherpaVadEngine.cs
├── Diarization/                # Phase 2
│   ├── IDiarizationEngine.cs
│   ├── SherpaDiarizationEngine.cs
│   ├── SpeakerProfile.cs
│   └── SpeakerProfileStore.cs
├── CommandRouting/             # Phase 2
│   ├── ICommandRouter.cs
│   ├── CommandPatternRouter.cs
│   ├── CommandPattern.cs
│   ├── CommandRouteResult.cs
│   └── SkillDispatcher.cs
├── Tts/                        # Phase 3
│   ├── ITtsEngine.cs
│   ├── QwenTtsEngine.cs
│   ├── ChatterboxTtsEngine.cs
│   ├── TtsCache.cs
│   └── TtsResult.cs
├── Pipeline/                   # Phase 4
│   ├── VoicePipeline.cs
│   ├── PipelineSession.cs
│   └── PipelineMetrics.cs
├── Discovery/
│   └── ZeroconfAdvertiser.cs
├── Models/
│   ├── ModelManager.cs          # Download, validate, load models
│   ├── ModelConfiguration.cs    # Model path + options config
│   ├── ModelCatalogService.cs   # Browse/download from sherpa-onnx releases
│   ├── ModelDownloader.cs       # HTTP download + extraction of tar.bz2
│   └── AsrModelDefinition.cs    # Model metadata definition
└── Extensions/
    └── ServiceCollectionExtensions.cs  # builder.AddWyomingServer()
```

### Modified Projects

```
lucia.AgentHost/
├── Program.cs                  # Add builder.AddWyomingServer() + Wyoming API endpoints
└── Apis/
    └── SpeakerApi.cs           # Phase 2: speaker enrollment endpoints

lucia.AppHost/
└── AppHost.cs                  # Expose Wyoming TCP port on AgentHost

lucia.Agents/                   # Minimal changes
├── Abstractions/
│   └── ICommandPatternProvider.cs  # New: Skills expose command patterns
└── Skills/
    ├── LightControlSkill.cs    # Add command pattern registration
    ├── ClimateControlSkill.cs  # Add command pattern registration
    └── SceneControlSkill.cs    # Add command pattern registration

lucia.Tests/
└── Wyoming/                    # New test directory for direct library testing
    ├── WyomingEventParserTests.cs
    ├── WyomingEventWriterTests.cs
    ├── CommandPatternRouterTests.cs
    └── AudioPipelineTests.cs
```

## Complexity Tracking

| Phase | New Files | Modified Files | Estimated Complexity | Key Risks |
|-------|-----------|----------------|---------------------|-----------|
| Phase 1 | ~20 | 3 | Medium-High | Wyoming protocol correctness, sherpa-onnx integration |
| Phase 2 | ~8 | 6 | Medium | Pattern matching accuracy, diarization streaming |
| Phase 3 | ~6 | 2 | Medium | Chatterbox ONNX integration without wrapper, streaming TTS |
| Phase 4 | ~5 | 4 | Medium-Low | End-to-end latency, multi-satellite concurrency |

Complexity is reduced versus the separate-host approach because there is no inter-process HTTP bridge, no standalone Wyoming Dockerfile, and no additional Aspire orchestration surface to maintain.

## Dependencies

### New NuGet Packages
These packages are added to `lucia.Wyoming.csproj`, which is referenced by `lucia.AgentHost`.

| Package | Version | Purpose |
|---------|---------|---------|
| `org.k2fsa.sherpa.onnx` | 1.12.0+ | STT, wake word, VAD, speaker verification |
| `Microsoft.ML.OnnxRuntime` | 1.20.0+ | Direct ONNX Runtime inference for Qwen3-TTS and Chatterbox Turbo (Phase 3) |
| `Makaretu.Dns.Multicast` | 1.0.0+ | Zeroconf/mDNS advertisement |
| `NAudio` | 2.2.1+ | Audio format conversion/resampling |
| `SharpCompress` | 0.38.0+ | Extract .tar.bz2 model archives |

Qwen3-TTS and Chatterbox Turbo both run via direct ONNX Runtime only. Phase 3 includes porting the Qwen3-TTS tokenizer/front-end to C#; no Python sidecar or wrapper package is part of the design.

### Model Downloads (Not NuGet — runtime assets)
| Model | Size | Phase |
|-------|------|-------|
| sherpa-onnx-streaming-zipformer-en | ~80MB | Phase 1 |
| sherpa-onnx-kws-zipformer | ~40MB | Phase 1 |
| silero_vad.onnx | ~2MB | Phase 1 |
| Speaker embedding model | ~90MB | Phase 2 |
| Qwen3-TTS 0.6B ONNX | ~5.5GB | Phase 3 |
| Chatterbox Turbo ONNX | ~1.5GB | Phase 3 |

## Phase Delivery Order

```
Phase 1 ──────────────────────────────────────────▶ MVP: Paired Wyoming + STT + Wake
    │
    ├── Phase 2 ──────────────────────────────────▶ Fast-path commands
    │       │
    │       └── Phase 3 ──────────────────────────▶ TTS voice output
    │               │
    │               └── Phase 4 ──────────────────▶ Full pipeline + polish
```

Each phase is incrementally deliverable and valuable:
- **After Phase 1**: Paired Wyoming satellites can connect, authenticate, and transcribe speech with wake word detection (replaces Whisper in HA pipeline)
- **After Phase 2**: Common commands execute instantly without LLM latency, using speaker verification and a richer orchestrator invocation contract
- **After Phase 3**: Full voice loop with local TTS at native 24kHz output by default (replaces Piper in HA pipeline)
- **After Phase 4**: Production-ready, optimized, multi-satellite voice assistant

## Configuration Model

The Wyoming configuration is nested directly under `lucia.AgentHost`'s `appsettings.json`; no separate Wyoming host-to-host configuration is required.

```json
{
  "Wyoming": {
    "Port": 10400,
    "Host": "0.0.0.0",
    "MaxWakeWordStreams": 30,
    "MaxConcurrentSttSessions": 4,
    "MaxConcurrentTtsSyntheses": 2,
    "ServiceName": "lucia-wyoming",
    "Models": {
      "BasePath": "/models",
      "Stt": {
        "ActiveModel": "sherpa-onnx-streaming-zipformer-en-2023-06-26",
        "ModelBasePath": "/models/stt",
        "NumThreads": 4,
        "SampleRate": 16000,
        "AvailableModels": {
          "sherpa-onnx-streaming-zipformer-en-2023-06-26": {
            "Type": "streaming-transducer",
            "Languages": ["en"],
            "Description": "Default English streaming model",
            "DownloadUrl": "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-2023-06-26.tar.bz2",
            "IsDefault": true
          }
        },
        "ModelCatalogUrl": "https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models",
        "AllowCustomModels": true
      },
      "WakeWord": {
        "Engine": "sherpa-kws",
        "ModelPath": "kws-zipformer",
        "Keywords": ["hey lucia"],
        "Sensitivity": 0.5
      },
      "Vad": {
        "Model": "silero_vad.onnx",
        "Threshold": 0.5,
        "MinSpeechDuration": 0.25,
        "MinSilenceDuration": 0.5
      },
      "Tts": {
        "PrimaryEngine": "qwen3-tts",
        "FallbackEngine": "chatterbox-turbo",
        "DefaultVoice": "ryan",
        "DefaultLanguage": "english",
        "CacheEnabled": true,
        "CacheMaxEntries": 500
      },
      "Diarization": {
        "Enabled": true,
        "SegmentationModel": "pyannote-segmentation-3-0.onnx",
        "EmbeddingModel": "3dspeaker_eres2net.onnx",
        "SpeakerThreshold": 0.7
      }
    },
    "CommandRouting": {
      "Enabled": true,
      "ConfidenceThreshold": 0.8,
      "FallbackToLlm": true
    }
  }
}
```

## Capacity Model

The Wyoming server handles two fundamentally different workload types:

### Always-On: Wake Word Streams
Every connected satellite maintains a continuous audio stream for wake word detection. These are lightweight and long-lived:
- **Per stream**: ~5MB (decoder state + audio buffer + VAD)
- **CPU**: ~3-5% of one core per stream
- **Duration**: Hours/days (as long as satellite is connected)
- **Default limit**: 30 concurrent streams

### Burst: STT / Diarization / TTS Sessions
Triggered only when a wake word fires. These are heavyweight but short-lived:
- **STT session**: ~50MB, ~25-50% of one core, lasts 5-10 seconds
- **Speaker embedding**: ~20MB, burst inference, < 200ms
- **TTS synthesis**: ~6GB VRAM (shared model), sequential per request, 0.5-3 seconds
- **Default limits**: 4 concurrent STT, 2 concurrent TTS

### Reference Hardware Capacity (RTX 3080 12GB / 32GB RAM / 8-core)

| Resource | Always-On Wake (30 streams) | Burst STT (4 sessions) | TTS Model | Total |
|----------|---------------------------|----------------------|-----------|-------|
| RAM | ~150MB | ~200MB | ~1GB (CPU fallback) | ~1.5GB |
| VRAM | — | — | ~6GB (Qwen3-TTS) | ~6GB |
| CPU | ~1.5 cores | ~2 cores (burst) | ~0.5 cores | ~4 cores peak |

This leaves ample headroom for AgentHost itself (~1GB RAM, 1-2 cores) and OS/other services.

### Scaling Notes
- A typical home with 3-10 satellites uses < 10% of the capacity budget
- Large installations (20+ rooms) remain comfortable on reference hardware
- Wake word is the dominant always-on cost but scales linearly and cheaply
- TTS is the GPU bottleneck — queue with backpressure for concurrent requests

## Testing Strategy

Tests live under `lucia.Tests/Wyoming/` and exercise the `lucia.Wyoming` library directly rather than going through a standalone host process.

### Unit Tests (per phase)
- Wyoming protocol parser/writer round-trip
- Audio buffer management
- Command pattern matching
- Speaker embedding comparison
- TTS cache eviction

### Integration Tests
- Wyoming TCP client → server connection lifecycle
- Full STT pipeline: WAV file → Wyoming chunks → transcript
- Wake word detection with test audio
- Command routing with mock skill dispatch
- TTS synthesis → Wyoming audio response

### End-to-End Tests (Phase 4)
- Simulated satellite session with recorded audio
- Multi-satellite concurrent sessions
- Pipeline latency benchmarks
- 24-hour stability soak test
