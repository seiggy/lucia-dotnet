# Phase 4: Full Integration, Multi-Satellite Support & Performance

**Phase**: 4 of 4
**Priority**: P2 — Production Readiness
**Dependencies**: Phase 1 (Wyoming Core), Phase 2 (Diarization), Phase 3 (TTS)
**Estimated Complexity**: Medium

## Objective

Integrate all voice pipeline components into a seamless end-to-end experience. Support multiple concurrent Wyoming satellites with independent sessions. Optimize latency, resource usage, and reliability for production deployment. Add comprehensive observability and graceful degradation.

---

## Deliverables

### D1: Full Voice Pipeline Orchestrator

**What**: Single coordinator that manages the complete wake → STT → route → execute → TTS → playback cycle

**Implementation Details**:

```csharp
public sealed class VoicePipeline
{
    private readonly IWakeWordDetector _wakeWord;
    private readonly ISttEngine _stt;
    private readonly IVadEngine _vad;
    private readonly IDiarizationEngine _diarization;
    private readonly ICommandRouter _commandRouter;
    private readonly TtsEngineSelector _ttsSelector;
    private readonly WyomingEventWriter _eventWriter;
    
    public async Task RunPipelineAsync(
        WyomingSession session, CancellationToken ct)
    {
        using var activity = Telemetry.StartActivity("voice-pipeline");
        
        // Stage 1: Wake Word Detection
        using (var wakeSpan = Telemetry.StartActivity("wake-word-detection"))
        {
            var detection = await WaitForWakeWordAsync(session, ct);
            wakeSpan?.SetTag("wake.keyword", detection.Keyword);
        }
        
        // Stage 2: Speech-to-Text
        string transcript;
        ReadOnlyMemory<float> utteranceAudio;
        using (var sttSpan = Telemetry.StartActivity("speech-to-text"))
        {
            (transcript, utteranceAudio) = await TranscribeUtteranceAsync(session, ct);
            sttSpan?.SetTag("stt.text", transcript);
            sttSpan?.SetTag("stt.duration_ms", utteranceAudio.Length / 16.0);
        }
        
        // Stage 3: Diarization + Command Routing (parallel)
        CommandRouteResult routeResult;
        using (var routeSpan = Telemetry.StartActivity("command-routing"))
        {
            var speakerTask = Task.Run(() => 
                _diarization.ExtractEmbedding(utteranceAudio.Span, 16000));
            var matchTask = _commandRouter.RouteAsync(transcript, ct);
            
            await Task.WhenAll(speakerTask, matchTask);
            
            var speaker = _diarization.IdentifySpeaker(speakerTask.Result);
            routeResult = matchTask.Result with { SpeakerId = speaker?.Name };
            
            routeSpan?.SetTag("route.is_fast_path", routeResult.IsMatch);
            routeSpan?.SetTag("route.confidence", routeResult.Confidence);
            routeSpan?.SetTag("route.speaker", speaker?.Name ?? "unknown");
        }
        
        // Stage 4: Execute Command
        string responseText;
        using (var execSpan = Telemetry.StartActivity("command-execution"))
        {
            responseText = routeResult.IsMatch
                ? await ExecuteFastPathAsync(routeResult, ct)
                : await ExecuteViaLlmAsync(transcript, routeResult, ct);
            
            execSpan?.SetTag("exec.response_length", responseText.Length);
        }
        
        // Stage 5: TTS Response
        using (var ttsSpan = Telemetry.StartActivity("tts-synthesis"))
        {
            await SynthesizeAndStreamAsync(session, responseText, ct);
            ttsSpan?.SetTag("tts.engine", _ttsSelector.LastUsedEngine);
        }
        
        // Stage 6: Continue conversation?
        if (session.ShouldContinueConversation)
        {
            // Return to STT listening (skip wake word)
            await RunFollowUpAsync(session, ct);
        }
    }
}
```

**Files**:
- `lucia.Wyoming/Pipeline/VoicePipeline.cs`
- `lucia.Wyoming/Pipeline/PipelineSession.cs`

---

### D2: Conversation Continuity

**What**: Support multi-turn conversations with `continue_conversation` semantics

**Implementation Details**:

#### Flow
1. Agent response includes `NeedsInput` flag (existing Lucia behavior)
2. Wyoming server sends TTS audio
3. After audio playback completes, server transitions directly to STT mode (no wake word needed)
4. Subsequent transcriptions are sent with the same Lucia `contextId` and `sessionId`
5. Continue until agent signals conversation complete

#### Session Mapping
```csharp
public sealed class PipelineSession
{
    public required string WyomingConnectionId { get; init; }
    public string? LuciaContextId { get; set; }
    public string? LuciaSessionId { get; set; }
    public string? SatelliteId { get; set; }
    public string? SpeakerId { get; set; }
    public bool ContinueConversation { get; set; }
    public PipelineStage CurrentStage { get; set; }
    public DateTimeOffset LastActivity { get; set; }
}

public enum PipelineStage
{
    WakeListening,
    Transcribing,
    Routing,
    Executing,
    Synthesizing,
    WaitingForFollowUp,
    Idle
}
```

#### Timeout Handling
- Follow-up listening times out after configurable duration (default 10 seconds)
- On timeout, return to wake word detection mode
- Emit `audio-stop` event to signal end of listening window

**Files**:
- `lucia.Wyoming/Pipeline/PipelineSession.cs`
- `lucia.Wyoming/Pipeline/ConversationManager.cs`

---

### D3: Multi-Satellite Support

**What**: Handle concurrent satellite connections with independent state

**Implementation Details**:

Because the Wyoming server runs in-process inside `lucia.AgentHost`, satellite sessions, routing, and skill dispatch all share the same DI container and memory space. There is no separate IPC boundary between Wyoming session management and Lucia command execution.

#### Per-Satellite Configuration
```json
{
  "Wyoming": {
    "Satellites": {
      "kitchen-satellite": {
        "DefaultVoice": "emma",
        "DefaultLanguage": "english",
        "WakeWords": ["hey lucia", "computer"],
        "TtsEngine": "chatterbox-turbo",
        "CommandAuthRequired": false
      },
      "bedroom-satellite": {
        "DefaultVoice": "ryan",
        "WakeWords": ["hey lucia"],
        "TtsEngine": "qwen3-tts",
        "CommandAuthRequired": true
      }
    }
  }
}
```

#### Resource Management
- Each satellite connection gets its own pipeline instance
- STT sessions share the same `OnlineRecognizer` (thread-safe, separate streams)
- Wake word sessions share the `KeywordSpotter` (same pattern, separate streams)
- TTS synthesis is queued through a `Channel<TtsSynthesisRequest>` with configurable concurrency
- Fast-path dispatch stays in-process, so there are no extra IPC concerns or loopback HTTP hops per satellite
- Memory budget tracking per connection with backpressure

#### Satellite Identification
- Identify satellites via Wyoming `describe` exchange or Zeroconf metadata
- Map satellite ID to configuration
- Unknown satellites use default configuration

**Files**:
- `lucia.Wyoming/Pipeline/SatelliteManager.cs`
- `lucia.Wyoming/Pipeline/SatelliteConfiguration.cs`

---

### D4: Graceful Degradation

**What**: System remains functional when optional components are unavailable

**Implementation Details**:

| Missing Component | Behavior |
|-------------------|----------|
| TTS models | STT + command routing works; responses returned as text only (no `synthesize` in `info`) |
| Diarization model | All commands routed to LLM (fast-path disabled); no speaker identification |
| Wake word model | STT-only mode (must be triggered externally); `detect` returns `error` |
| STT model | Server starts but reports unhealthy; only TTS synthesis available |
| Lucia engine / skill resolution failure | Fast-path returns a local error response and the request can fall back to the standard Lucia orchestration path |
| GPU unavailable | Falls back to CPU inference; logs warning about increased latency |

#### Health Check Integration
The Wyoming health check plugs into AgentHost's existing health check pipeline instead of running as a separate host/service.

```csharp
public sealed class WyomingHealthCheck(
    ISttEngine sttEngine,
    IWakeWordDetector wakeWord,
    IEnumerable<ITtsEngine> ttsEngines,
    IDiarizationEngine diarization,
    ISatelliteManager sessionManager) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct)
    {
        var data = new Dictionary<string, object>
        {
            ["stt_ready"] = sttEngine.IsReady,
            ["wake_word_ready"] = wakeWord.IsReady,
            ["tts_ready"] = ttsEngines.Any(e => e.IsReady),
            ["diarization_ready"] = diarization.IsReady,
            ["active_sessions"] = sessionManager.ActiveCount,
        };

        return Task.FromResult(sttEngine.IsReady
            ? HealthCheckResult.Healthy("Wyoming server (in AgentHost process) operational", data)
            : HealthCheckResult.Unhealthy("STT engine not ready", data: data));
    }
}

// lucia.AgentHost/Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<WyomingHealthCheck>("wyoming");
```

**Files**:
- `lucia.Wyoming/Pipeline/DegradationManager.cs`
- `lucia.Wyoming/Health/WyomingHealthCheck.cs`
- `lucia.AgentHost/Program.cs` (register with existing health checks)

---

### D5: Comprehensive Telemetry

**What**: Full OpenTelemetry instrumentation for the voice pipeline

**Implementation Details**:

#### Traces
Every pipeline execution generates a distributed trace:
```
voice-pipeline (root span)
├── wake-word-detection
│   ├── audio-processing (per-chunk)
│   └── keyword-check
├── speech-to-text  
│   ├── vad-segmentation
│   ├── audio-resampling (if needed)
│   └── stt-decode
├── command-routing
│   ├── speaker-embedding-extraction
│   ├── speaker-identification
│   ├── pattern-matching
│   └── entity-resolution
├── command-execution
│   ├── fast-path-dispatch (or)
│   └── llm-orchestrator-call
└── tts-synthesis
    ├── engine-selection
    ├── cache-check
    ├── synthesis
    └── audio-streaming
```

#### Metrics
| Metric | Type | Labels |
|--------|------|--------|
| `wyoming.sessions.active` | Gauge | satellite_id |
| `wyoming.stt.duration_ms` | Histogram | model, language |
| `wyoming.stt.word_count` | Histogram | model |
| `wyoming.wake.detections` | Counter | keyword |
| `wyoming.wake.false_positives` | Counter | keyword |
| `wyoming.routing.fast_path` | Counter | skill |
| `wyoming.routing.llm_fallback` | Counter | reason |
| `wyoming.routing.confidence` | Histogram | skill |
| `wyoming.tts.duration_ms` | Histogram | engine, voice |
| `wyoming.tts.cache_hits` | Counter | - |
| `wyoming.pipeline.e2e_latency_ms` | Histogram | route_type |
| `wyoming.diarization.speaker_identified` | Counter | speaker |

**Files**:
- `lucia.Wyoming/Pipeline/PipelineMetrics.cs`
- `lucia.Wyoming/Pipeline/PipelineTelemetry.cs`

---

### D6: Performance Optimization

**What**: Tune latency and resource usage for production

**Implementation Details**:

In-process hosting eliminates HTTP serialization / deserialization overhead between Wyoming command routing and Lucia command execution, making fast-path dispatch even faster.

#### Latency Budget
| Stage | Target | Concurrency | Optimization |
|-------|--------|-------------|-------------|
| Wake word detection | < 300ms | 20-30 always-on | Lightweight KWS, continuous |
| VAD segmentation | < 50ms | Per wake trigger | Parallel with buffering |
| STT transcription | < 500ms | 1-4 burst | Streaming decode |
| Speaker embedding | < 100ms | Per utterance | Parallel with pattern match |
| Pattern matching | < 50ms | Per utterance | Pre-compiled regex |
| Fast-path dispatch | < 100ms | Per utterance | Direct in-process |
| LLM orchestration | 1-3s | 1-2 concurrent | Streaming response |
| TTS synthesis | < 500ms TTFA | 1-2 queued | Cache common phrases |

#### Memory Optimization
- Pool audio buffers using `ArrayPool<byte>.Shared`
- Reuse ONNX inference sessions (sherpa-onnx handles this internally)
- Limit concurrent TTS synthesis (queue with backpressure)
- Monitor RSS via health check; alert at 80% of budget

#### Always-On Wake Word Optimization
- Wake word streams are the dominant always-on cost — optimize aggressively
- Pool audio buffers across wake streams using `ArrayPool<byte>.Shared`
- Share the `KeywordSpotter` model instance across all streams (thread-safe with separate stream objects)
- Use `SemaphoreSlim` to gate STT/TTS burst sessions, not wake word streams
- Monitor per-stream memory via health check; alert if a single stream exceeds 10MB

#### CPU Optimization
- Pin sherpa-onnx thread count to configured value (not unlimited)
- Use `ThreadPool` for audio processing, not dedicated threads
- Avoid unnecessary audio copies (use `Span<T>`/`Memory<T>`)

**Files**:
- `lucia.Wyoming/Audio/AudioBufferPool.cs`

---

### D7: Dockerfile & Container Build

**What**: Update the existing AgentHost container image to include Wyoming server runtime requirements

**Implementation Details**:

Modify the existing `lucia.AgentHost/Dockerfile`; do not introduce a standalone Wyoming Dockerfile.

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish lucia.AgentHost -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install native dependencies for sherpa-onnx
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Copy application
COPY --from=build /app/publish .

# Wyoming / sherpa model volume mount point
VOLUME /models

# Existing AgentHost HTTP port + Wyoming TCP port
EXPOSE 8080/tcp
EXPOSE 10400/tcp

HEALTHCHECK --interval=30s CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "lucia.AgentHost.dll"]
```

- Keep the existing AgentHost entrypoint and deployment model
- The unified container exposes both the HTTP API surface and the Wyoming TCP listener
- Any optional model pre-loading happens in this same AgentHost image

**Files**:
- `lucia.AgentHost/Dockerfile`

---

### D8: Integration Tests

**What**: End-to-end tests validating the full pipeline

**Test Cases**:

#### Full Pipeline Tests
- `Pipeline_WakeToSttToFastPathToTts_EndToEnd`
- `Pipeline_WakeToSttToLlmToTts_EndToEnd`
- `Pipeline_ContinueConversation_MultiTurn`
- `Pipeline_MultipleSatellites_Concurrent`
- `Pipeline_SatelliteDisconnect_Cleanup`

#### Performance Tests
- `Pipeline_FastPathLatency_Under1500ms`
- `Pipeline_LlmPathLatency_Under5000ms`
- `Pipeline_ConcurrentSessions_NoResourceLeak`
- `Pipeline_24HourSoak_StableMemory`

#### Degradation Tests
- `Pipeline_NoTtsModel_SttStillWorks`
- `Pipeline_NoDiarization_RoutesToLlm`
- `Pipeline_LuciaEngineFailure_DegradesGracefully`
- `Pipeline_GpuUnavailable_FallsToCpu`

**Files**:
- `lucia.Tests/Wyoming/VoicePipelineTests.cs`
- `lucia.Tests/Wyoming/MultiSatelliteTests.cs`
- `lucia.Tests/Wyoming/DegradationTests.cs`

---

## Task Breakdown

| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P4-PIPE-001 | Implement VoicePipeline | No | End-to-end pipeline orchestrator |
| P4-PIPE-002 | Implement PipelineSession | Yes | Session state model |
| P4-PIPE-003 | Implement ConversationManager | No | Multi-turn conversation with continue semantics |
| P4-SAT-001 | Implement SatelliteManager | Yes | Multi-satellite configuration and routing |
| P4-SAT-002 | Implement SatelliteConfiguration | Yes | Per-satellite settings model |
| P4-DEG-001 | Implement DegradationManager | No | Graceful feature degradation |
| P4-DEG-002 | Implement WyomingHealthCheck | Yes | Comprehensive health reporting |
| P4-TEL-001 | Implement PipelineMetrics | Yes | OpenTelemetry metrics |
| P4-TEL-002 | Implement PipelineTelemetry | No | Distributed tracing spans |
| P4-PERF-001 | Implement AudioBufferPool | Yes | Memory pooling optimization |
| P4-PERF-002 | Latency optimization pass | No | Profile and tune each stage |
| P4-DOCK-001 | Update AgentHost Dockerfile | Yes | Unified production container build |
| P4-TEST-001 | Write integration tests | No | Full pipeline tests |
| P4-TEST-002 | Write performance tests | No | Latency and stability benchmarks |
| P4-TEST-003 | Write degradation tests | No | Graceful degradation scenarios |

---

## Acceptance Criteria

Phase 4 is complete when:
1. ✅ Full wake → STT → route → execute → TTS pipeline works end-to-end
2. ✅ `continue_conversation` enables multi-turn voice interactions
3. ✅ Multiple satellites operate concurrently with independent sessions
4. ✅ Fast-path commands complete in < 1.5s end-to-end
5. ✅ LLM-routed commands complete in < 5s end-to-end
6. ✅ System degrades gracefully when components are unavailable
7. ✅ All pipeline stages have OpenTelemetry spans and metrics
8. ✅ Unified AgentHost container builds and runs with `/models` volume plus HTTP and Wyoming TCP ports exposed
9. ✅ 24-hour soak test passes without memory leaks
10. ✅ All integration, performance, and degradation tests pass
