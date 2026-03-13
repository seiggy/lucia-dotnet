# Phase 1: Wyoming Protocol Core + Streaming STT + Wake Word Detection

**Phase**: 1 of 4
**Priority**: P0 — Foundation
**Dependencies**: None (greenfield)
**Estimated Complexity**: High

## Objective

Deliver a working Wyoming protocol server in C#/.NET that can:
1. Accept TCP connections from Wyoming-compatible satellites
2. Perform streaming speech-to-text using sherpa-onnx
3. Detect wake words using sherpa-onnx keyword spotter
4. Integrate with Aspire orchestration and service discovery

This phase establishes the entire audio transport layer and core speech processing pipeline.

---

## Deliverables

### D1: `lucia.Wyoming` Project Scaffold

**What**: New class library hosted in-process inside `lucia.AgentHost`

**Implementation Details**:
- Create `lucia.Wyoming` as a class library targeting `net10.0`
- Add to `lucia-dotnet.slnx` solution
- Reference `lucia.ServiceDefaults` for telemetry and health check conventions
- Reference `lucia.Agents` for direct access to Lucia skills and orchestration engine
- Add DI registration extension in `lucia.Wyoming/Extensions/ServiceCollectionExtensions.cs`
- Integrate in `lucia.AgentHost/Program.cs`:
  ```csharp
  // In lucia.AgentHost/Program.cs
  builder.AddWyomingServer();  // Registers all Wyoming services + IHostedService
  ```
- Update `lucia.AppHost/AppHost.cs` to expose the Wyoming TCP port on the existing AgentHost resource (no new project reference):
  ```csharp
  var agentHost = builder.AddProject<Projects.lucia_AgentHost>("lucia-agenthost")
      // ... existing config ...
      .WithEndpoint(port: 10400, name: "wyoming-tcp", scheme: "tcp");
  ```

**Files**:
- `lucia.Wyoming/lucia.Wyoming.csproj`
- `lucia.Wyoming/Extensions/ServiceCollectionExtensions.cs`

**Modified**:
- `lucia.AgentHost/Program.cs`
- `lucia.AppHost/AppHost.cs`

---

### D2: Wyoming Protocol Implementation

**What**: Full Wyoming protocol TCP server with event parsing and serialization

**Implementation Details**:

#### TCP Server (`WyomingServer.cs`)
- `IHostedService` that manages a `TcpListener`
- Registered via `builder.AddWyomingServer()` in `lucia.AgentHost`'s DI container
- Shares the `lucia.AgentHost` process lifetime
- Accepts connections on configurable port (default: 10400)
- Spawns a `WyomingSession` per connection
- Tracks active sessions with concurrent dictionary
- Uses explicit concurrency gates for burst STT and TTS work, not for always-on wake word streams
- Graceful shutdown: close all sessions on `StopAsync`

#### Two-Tier Connection Architecture

Each satellite TCP connection operates in two tiers:

**Tier 1: Always-On Wake Word Stream**
- Established when satellite connects
- Continuous audio streaming from satellite → server
- Lightweight KWS processing runs for the lifetime of the connection
- One `IWakeWordSession` per connection, running continuously
- Resource budget: ~5MB RAM, ~3-5% of one core

**Tier 2: Burst Processing Session (on-demand)**
- Activated when wake word fires
- STT session created, transcription runs for the utterance duration (5-10s)
- Speaker embedding extracted (< 200ms)
- Command routing + execution (< 200ms fast-path, 1-3s LLM)
- Optional TTS response synthesis
- Session torn down after response, returns to Tier 1 listening
- Resource budget: ~50-100MB RAM, ~50% of one core (for duration of utterance)

This means a satellite with the wake word listening costs almost nothing until someone actually speaks — then it briefly uses significant resources for the STT burst, and immediately returns to lightweight wake listening.

```csharp
public sealed class WyomingServer : IHostedService, IDisposable
{
    private TcpListener? _listener;
    private readonly ConcurrentDictionary<string, WyomingSession> _sessions = new();
    private readonly SemaphoreSlim _sttConcurrency; // limits concurrent STT sessions
    private readonly SemaphoreSlim _ttsConcurrency; // limits concurrent TTS syntheses
    // No semaphore for wake word — all connected satellites get always-on KWS
    
    public async Task StartAsync(CancellationToken ct)
    {
        _listener = new TcpListener(IPAddress.Parse(_options.Host), _options.Port);
        _listener.Start();
        _ = AcceptConnectionsAsync(ct);
    }
    
    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = await _listener!.AcceptTcpClientAsync(ct);
            var session = new WyomingSession(client, _serviceProvider, _sttConcurrency, _ttsConcurrency);
            _sessions.TryAdd(session.Id, session);
            _ = session.RunAsync(ct);
        }
    }
}
```

#### Event Parser (`WyomingEventParser.cs`)
- Reads newline-delimited JSON from `NetworkStream`
- Deserializes header using `System.Text.Json`
- Reads `data_length` bytes of extra data if present
- Reads `payload_length` bytes of binary payload if present
- Returns strongly-typed `WyomingEvent` objects

```csharp
public sealed class WyomingEventParser
{
    public async Task<WyomingEvent?> ReadEventAsync(
        Stream stream, CancellationToken ct)
    {
        // Read JSON header line
        var headerLine = await ReadLineAsync(stream, ct);
        if (headerLine is null) return null;
        
        var header = JsonSerializer.Deserialize<WyomingEventHeader>(headerLine);
        
        // Read optional extra data
        byte[]? extraData = null;
        if (header.DataLength > 0)
            extraData = await ReadExactAsync(stream, header.DataLength, ct);
        
        // Read optional binary payload
        byte[]? payload = null;
        if (header.PayloadLength > 0)
            payload = await ReadExactAsync(stream, header.PayloadLength, ct);
        
        return WyomingEvent.Create(header.Type, header.Data, extraData, payload);
    }
}
```

#### Event Writer (`WyomingEventWriter.cs`)
- Serializes `WyomingEvent` to JSON header + binary payload
- Writes to `NetworkStream` atomically (use `SemaphoreSlim` for write serialization)

#### Event Types (`WyomingEvent.cs`)
- Base `WyomingEvent` with `Type`, `Data`, `Payload` properties
- Derived types for each event: `AudioStartEvent`, `AudioChunkEvent`, `AudioStopEvent`, `TranscribeEvent`, `TranscriptEvent`, `DetectEvent`, `DetectionEvent`, `DescribeEvent`, `InfoEvent`, `ErrorEvent`, etc.
- Factory method `WyomingEvent.Create(type, data, payload)` for deserialization

#### Session (`WyomingSession.cs`)
- Manages lifecycle of a single TCP connection
- Maintains an always-on Tier 1 wake listening loop and promotes into Tier 2 burst processing only after wake detection
- State machine: `Idle → WakeListening → Transcribing → Processing → Responding`
- Dispatches events to appropriate handlers (STT engine, wake word detector)
- Handles `describe` → responds with `info` listing capabilities
- Validates the presented device token immediately after the initial `describe`/`info` exchange and rejects unpaired satellites before `detect`, `transcribe`, or other operational events are processed
- Tracks audio format from `audio-start` for the session
- Cleans up resources on disconnect

**Files**:
- `lucia.Wyoming/Wyoming/WyomingServer.cs`
- `lucia.Wyoming/Wyoming/WyomingSession.cs`
- `lucia.Wyoming/Wyoming/WyomingEventParser.cs`
- `lucia.Wyoming/Wyoming/WyomingEventWriter.cs`
- `lucia.Wyoming/Wyoming/WyomingEvent.cs`
- `lucia.Wyoming/Wyoming/WyomingEventHeader.cs`
- `lucia.Wyoming/Wyoming/WyomingServiceInfo.cs`
- `lucia.Wyoming/Wyoming/WyomingSessionState.cs`

---

### D2a: Device Pairing & Trusted Satellites

**What**: Require Wyoming satellites to pair before they can use the TCP service

**Implementation Details**:
- `PairingService` stores paired device records and tokens in MongoDB for lookup during connection setup
- The dashboard pairing page uses AgentHost REST APIs to create, list, and revoke trusted satellites
- `WyomingSession` allows the initial `describe`/`info` exchange, then validates the presented device token before accepting `detect`, `transcribe`, or other operational events
- Unpaired or invalid devices receive a Wyoming `error` response and the connection is closed immediately after the pairing check
- Pairing is Phase 1 scope: only paired satellites are allowed to stay connected to the Wyoming listener

#### REST API
- `POST /api/wyoming/devices/pair` — Pair a new device token and friendly name
- `GET /api/wyoming/devices` — List paired devices for the dashboard
- `DELETE /api/wyoming/devices/{id}` — Remove a paired device and revoke its token

**Files**:
- `lucia.Wyoming/Security/PairingService.cs`
- `lucia.Wyoming/Security/PairedDevice.cs`
- `lucia.AgentHost/Apis/WyomingDeviceApi.cs`
- `lucia.Wyoming/Wyoming/WyomingSession.cs` (modify)

---

### D3: Voice Activity Detection (VAD)

**What**: Silero VAD via sherpa-onnx to segment speech from silence

**Implementation Details**:

#### Interface
```csharp
public interface IVadEngine : IDisposable
{
    void AcceptAudioChunk(ReadOnlySpan<float> samples);
    bool HasSpeechSegment { get; }
    VadSegment GetNextSegment();
    void Reset();
}
```

#### sherpa-onnx Implementation
- Wraps `VoiceActivityDetector` from sherpa-onnx
- Configurable threshold, min speech/silence duration
- Emits `VadSegment` objects with start/end timestamps and audio data
- Used as pre-filter before STT to avoid processing silence

**Files**:
- `lucia.Wyoming/Vad/IVadEngine.cs`
- `lucia.Wyoming/Vad/SherpaVadEngine.cs`
- `lucia.Wyoming/Vad/VadSegment.cs`

---

### D4: Streaming Speech-to-Text

**What**: Real-time ASR using sherpa-onnx streaming recognizer

**Implementation Details**:

#### Interface
```csharp
public interface ISttEngine : IDisposable
{
    ISttSession CreateSession();
}

public interface ISttSession : IDisposable
{
    void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate);
    SttResult GetPartialResult();
    SttResult GetFinalResult();
    bool IsEndOfUtterance { get; }
}
```

#### sherpa-onnx Implementation
- Wraps `OnlineRecognizer` and `OnlineStream`
- Creates one `OnlineStream` per STT session (per utterance)
- Feeds audio chunks incrementally
- Calls `Decode()` after each chunk to get partial results
- Returns `SttResult` with text, confidence, timing info
- Thread-safe: each session has its own stream instance

#### Audio Format Handling
- Accepts 16-bit PCM from Wyoming chunks
- Converts to float32 samples for sherpa-onnx
- Handles sample rate mismatch via `AudioResampler`

**Files**:
- `lucia.Wyoming/Stt/ISttEngine.cs`
- `lucia.Wyoming/Stt/ISttSession.cs`
- `lucia.Wyoming/Stt/SherpaSttEngine.cs`
- `lucia.Wyoming/Stt/SherpaSttSession.cs`
- `lucia.Wyoming/Stt/SttResult.cs`

---

### D5: Wake Word Detection

**What**: Always-on keyword spotting using sherpa-onnx

**Implementation Details**:

#### Interface
```csharp
public interface IWakeWordDetector : IDisposable
{
    IWakeWordSession CreateSession();
}

public interface IWakeWordSession : IDisposable
{
    void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate);
    WakeWordResult? CheckForDetection();
    void Reset();
}
```

#### sherpa-onnx Implementation
- Wraps `KeywordSpotter` and stream
- Feeds continuous audio from Wyoming connection
- Returns keyword name when detected
- Low CPU overhead for always-on operation
- Configurable keywords via `keywords.txt` file

#### Custom Wake Words
- Support user-defined wake words (e.g., "Hey Lucia", "Computer", custom names)
- Keywords file generated from configuration at startup
- Sensitivity per-keyword configurable

**Files**:
- `lucia.Wyoming/WakeWord/IWakeWordDetector.cs`
- `lucia.Wyoming/WakeWord/IWakeWordSession.cs`
- `lucia.Wyoming/WakeWord/SherpaWakeWordDetector.cs`
- `lucia.Wyoming/WakeWord/SherpaWakeWordSession.cs`
- `lucia.Wyoming/WakeWord/WakeWordResult.cs`

#### Custom Wake Word Manager

Manages user-defined wake words using sherpa-onnx's open-vocabulary keyword spotting:

```csharp
public sealed class CustomWakeWordManager
{
    private readonly KeywordSpotter _spotter;
    private readonly WakeWordTokenizer _tokenizer;
    private readonly ILogger<CustomWakeWordManager> _logger;
    
    /// <summary>
    /// Register a new custom wake word from plain text.
    /// No audio training required -- uses open-vocabulary KWS.
    /// </summary>
    public async Task<CustomWakeWord> RegisterWakeWordAsync(
        string phrase, string? userId = null, CancellationToken ct = default)
    {
        // Validate phrase (min 2 syllables, no profanity, etc.)
        ValidatePhrase(phrase);
        
        // Tokenize using model vocabulary
        var tokens = _tokenizer.Tokenize(phrase);
        
        var wakeWord = new CustomWakeWord
        {
            Id = Guid.NewGuid().ToString("N"),
            Phrase = phrase,
            Tokens = tokens,
            UserId = userId,
            BoostScore = 1.5f,   // Default: slightly boosted
            Threshold = 0.30f,   // Default: moderate sensitivity
            IsCalibrated = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        
        // Persist and reload keyword spotter
        await _wakeWordStore.SaveAsync(wakeWord, ct);
        await ReloadKeywordsAsync(ct);
        
        return wakeWord;
    }
    
    /// <summary>
    /// Calibrate a wake word using recorded audio samples.
    /// Adjusts boost score and threshold for optimal detection.
    /// </summary>
    public async Task<CalibrationResult> CalibrateAsync(
        string wakeWordId,
        IReadOnlyList<ReadOnlyMemory<float>> audioSamples,
        int sampleRate,
        CancellationToken ct)
    {
        var wakeWord = await _wakeWordStore.GetAsync(wakeWordId, ct);
        
        // Test each sample against current keyword spotter
        var detections = new List<CalibrationDetection>();
        foreach (var sample in audioSamples)
        {
            var stream = _spotter.CreateStream(
                $"{wakeWord.Tokens} :{wakeWord.BoostScore} #{wakeWord.Threshold}");
            
            stream.AcceptWaveform(sampleRate, sample.Span);
            
            float maxConfidence = 0f;
            bool detected = false;
            while (_spotter.IsReady(stream))
            {
                _spotter.Decode(stream);
                var result = _spotter.GetResult(stream);
                if (!string.IsNullOrEmpty(result.Keyword))
                {
                    detected = true;
                    maxConfidence = Math.Max(maxConfidence, result.Confidence);
                }
            }
            
            detections.Add(new CalibrationDetection
            {
                Detected = detected,
                Confidence = maxConfidence,
                AudioDurationMs = (int)(sample.Length / (sampleRate / 1000.0)),
            });
        }
        
        // Auto-tune parameters based on calibration results
        var (boost, threshold) = AutoTune(detections, wakeWord);
        
        wakeWord = wakeWord with
        {
            BoostScore = boost,
            Threshold = threshold,
            IsCalibrated = true,
            CalibratedAt = DateTimeOffset.UtcNow,
            CalibrationSamples = audioSamples.Count,
        };
        
        await _wakeWordStore.SaveAsync(wakeWord, ct);
        await ReloadKeywordsAsync(ct);
        
        return new CalibrationResult
        {
            DetectionRate = detections.Count(d => d.Detected) / (float)detections.Count,
            AverageConfidence = detections.Where(d => d.Detected).Average(d => d.Confidence),
            BoostScore = boost,
            Threshold = threshold,
            Recommendation = GenerateRecommendation(detections),
        };
    }
    
    private (float boost, float threshold) AutoTune(
        List<CalibrationDetection> detections, CustomWakeWord current)
    {
        float detectionRate = detections.Count(d => d.Detected) / (float)detections.Count;
        float avgConfidence = detections.Where(d => d.Detected)
            .Select(d => d.Confidence).DefaultIfEmpty(0).Average();
        
        float boost = current.BoostScore;
        float threshold = current.Threshold;
        
        if (detectionRate >= 1.0f && avgConfidence > 0.6f)
        {
            // All detected with high confidence -- tighten to reduce false positives
            boost = Math.Max(0.8f, boost - 0.3f);
            threshold = Math.Min(0.5f, threshold + 0.05f);
        }
        else if (detectionRate < 0.8f)
        {
            // Missing detections -- loosen to improve recall
            boost = Math.Min(3.0f, boost + 0.5f);
            threshold = Math.Max(0.15f, threshold - 0.05f);
        }
        
        return (boost, threshold);
    }
    
    /// <summary>
    /// Regenerate keywords.txt and reload the keyword spotter at runtime.
    /// </summary>
    private async Task ReloadKeywordsAsync(CancellationToken ct)
    {
        var allWords = await _wakeWordStore.GetAllAsync(ct);
        var keywordsContent = string.Join("\n",
            allWords.Select(w => $"{w.Tokens} :{w.BoostScore:F2} #{w.Threshold:F2}"));
        
        await File.WriteAllTextAsync(_keywordsFilePath, keywordsContent, ct);
        
        // Reload keyword spotter with new keywords
        // sherpa-onnx supports this without recreating the model
        _logger.LogInformation("Reloaded {Count} wake words", allWords.Count);
    }
}
```

#### Wake Word Tokenizer

Port of sherpa-onnx's text2token for in-process use:

```csharp
public sealed class WakeWordTokenizer
{
    private readonly Dictionary<string, int> _vocabulary;
    
    public WakeWordTokenizer(string tokensFilePath)
    {
        // Load tokens.txt from the KWS model directory
        _vocabulary = File.ReadAllLines(tokensFilePath)
            .Select((line, idx) => (line.Split(' ')[0], idx))
            .ToDictionary(x => x.Item1, x => x.idx);
    }
    
    /// <summary>
    /// Convert a plain text phrase to model token sequence.
    /// Uses greedy longest-match against the model vocabulary.
    /// </summary>
    public string Tokenize(string phrase)
    {
        // Normalize: uppercase, trim, collapse whitespace
        var normalized = phrase.Trim().ToUpperInvariant();
        
        // BPE-style tokenization using model vocabulary
        var tokens = new List<string>();
        // ... tokenization logic matching model's BPE vocabulary
        
        return string.Join(" ", tokens);
    }
}
```

**Files** (add to existing files list):
- `lucia.Wyoming/WakeWord/CustomWakeWordManager.cs`
- `lucia.Wyoming/WakeWord/WakeWordTokenizer.cs`
- `lucia.Wyoming/WakeWord/CustomWakeWord.cs`
- `lucia.Wyoming/WakeWord/WakeWordStore.cs`
- `lucia.Wyoming/WakeWord/CalibrationResult.cs`

---

### D6: Audio Processing Utilities

**What**: Shared audio processing infrastructure

**Implementation Details**:

#### Audio Buffer (`AudioBuffer.cs`)
- Ring buffer for streaming audio with configurable capacity
- Supports look-back for VAD context window
- Thread-safe for producer (network reader) / consumer (STT/VAD) pattern

#### Audio Resampler (`AudioResampler.cs`)
- Converts between sample rates (e.g., 48kHz → 16kHz)
- Uses linear interpolation or polyphase filter
- Stateful for streaming (maintains filter state across chunks)

#### Audio Format (`AudioFormat.cs`)
- Represents audio stream parameters (rate, width, channels)
- Parsing from Wyoming `audio-start` event data
- Format negotiation helpers

#### PCM Conversion
- Convert 16-bit PCM bytes to float32 samples and back
- Handle endianness

**Files**:
- `lucia.Wyoming/Audio/AudioBuffer.cs`
- `lucia.Wyoming/Audio/AudioResampler.cs`
- `lucia.Wyoming/Audio/AudioFormat.cs`
- `lucia.Wyoming/Audio/AudioPipeline.cs`
- `lucia.Wyoming/Audio/PcmConverter.cs`

---

### D7: Zeroconf Service Discovery

**What**: mDNS advertisement so Home Assistant auto-discovers the Wyoming server

**Implementation Details**:
- Advertise `_wyoming._tcp` service via mDNS
- Include service metadata (name, version, capabilities)
- Run as `IHostedService` alongside the TCP server
- Use `Makaretu.Dns.Multicast` NuGet package

**Files**:
- `lucia.Wyoming/Discovery/ZeroconfAdvertiser.cs`

---

### D8: Model Catalog & Download Manager

**What**: Comprehensive model management system that lets users browse, download, and switch between any supported sherpa-onnx ASR model

**Implementation Details**:

#### Model Catalog Service
A service that knows about all available sherpa-onnx ASR models and their metadata:

```csharp
public sealed class ModelCatalogService
{
    /// <summary>
    /// Built-in catalog of all known sherpa-onnx ASR models with metadata.
    /// Updated with each Lucia release.
    /// </summary>
    private static readonly IReadOnlyList<AsrModelDefinition> BuiltInCatalog =
    [
        new()
        {
            Id = "sherpa-onnx-streaming-zipformer-en-2023-06-26",
            Name = "Streaming Zipformer English",
            Architecture = ModelArchitecture.ZipformerTransducer,
            IsStreaming = true,
            Languages = ["en"],
            SizeBytes = 80_000_000,
            Description = "Default English streaming model. Best balance of speed and accuracy.",
            DownloadUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-zipformer-en-2023-06-26.tar.bz2",
            IsDefault = true,
            MinMemoryMb = 200,
            QuantizationVariants = [],
        },
        // ... all other models from the catalog
    ];
    
    public IReadOnlyList<AsrModelDefinition> GetAvailableModels(
        ModelFilter? filter = null)
    {
        var models = BuiltInCatalog.AsEnumerable();
        
        if (filter?.StreamingOnly == true)
            models = models.Where(m => m.IsStreaming);
        if (filter?.Language is not null)
            models = models.Where(m => m.Languages.Contains(filter.Language));
        if (filter?.Architecture is not null)
            models = models.Where(m => m.Architecture == filter.Architecture);
        if (filter?.MaxSizeMb is not null)
            models = models.Where(m => m.SizeBytes / 1_000_000 <= filter.MaxSizeMb);
        
        return models.ToList();
    }
    
    public IReadOnlyList<AsrModelDefinition> GetInstalledModels()
    {
        // Check which models exist on disk at ModelBasePath
        return BuiltInCatalog
            .Where(m => Directory.Exists(Path.Combine(_options.ModelBasePath, m.Id)))
            .ToList();
    }
}
```

#### Model Downloader
Downloads and extracts model archives from GitHub releases:

```csharp
public sealed class ModelDownloader(
    IHttpClientFactory httpFactory,
    ILogger<ModelDownloader> logger)
{
    public async Task<ModelDownloadResult> DownloadModelAsync(
        AsrModelDefinition model,
        string targetBasePath,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var targetDir = Path.Combine(targetBasePath, model.Id);
        if (Directory.Exists(targetDir))
            return ModelDownloadResult.AlreadyExists(targetDir);
        
        var tempArchive = Path.GetTempFileName();
        try
        {
            // Download .tar.bz2 archive
            using var client = httpFactory.CreateClient();
            using var response = await client.GetAsync(model.DownloadUrl, 
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(tempArchive);
            
            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloaded += bytesRead;
                progress?.Report(new ModelDownloadProgress
                {
                    ModelId = model.Id,
                    BytesDownloaded = downloaded,
                    TotalBytes = totalBytes,
                });
            }
            
            // Extract tar.bz2 to target directory
            fileStream.Close();
            await ExtractTarBz2Async(tempArchive, targetBasePath, ct);
            
            logger.LogInformation("Downloaded and extracted model {ModelId} to {Path}",
                model.Id, targetDir);
            
            return ModelDownloadResult.Success(targetDir);
        }
        finally
        {
            File.Delete(tempArchive);
        }
    }
    
    private static async Task ExtractTarBz2Async(
        string archivePath, string targetDir, CancellationToken ct)
    {
        // Use SharpCompress for tar.bz2 extraction
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.Open(stream);
        while (reader.MoveToNextEntry())
        {
            if (!reader.Entry.IsDirectory)
            {
                reader.WriteEntryToDirectory(targetDir, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true,
                });
            }
        }
    }
}
```

#### Model Manager (Updated)
Orchestrates model lifecycle — validation, loading, and hot-swapping:

```csharp
public sealed class ModelManager
{
    /// <summary>
    /// Validates that the active model exists and its files are complete.
    /// Reports missing models via health check.
    /// </summary>
    public async Task<ModelValidationResult> ValidateActiveModelAsync(CancellationToken ct);
    
    /// <summary>
    /// Auto-detects model type (transducer/CTC/paraformer/whisper) from files present
    /// and configures sherpa-onnx accordingly.
    /// </summary>
    public SherpaModelConfig BuildSherpaConfig(string modelPath);
    
    /// <summary>
    /// Switch the active STT model at runtime (requires brief STT interruption).
    /// </summary>
    public async Task SwitchActiveModelAsync(string modelId, CancellationToken ct);
}
```

#### Auto-Detection of Model Type
Different sherpa-onnx models use different architectures and file layouts. The model manager auto-detects:

```csharp
public ModelArchitecture DetectModelArchitecture(string modelDir)
{
    // Transducer models have encoder/decoder/joiner
    if (File.Exists(Path.Combine(modelDir, "encoder-epoch-99-avg-1.onnx")) ||
        File.Exists(Path.Combine(modelDir, "encoder.onnx")))
        return ModelArchitecture.ZipformerTransducer;
    
    // Paraformer models have model.onnx
    if (File.Exists(Path.Combine(modelDir, "model.onnx")) &&
        File.Exists(Path.Combine(modelDir, "tokens.txt")))
        return ModelArchitecture.Paraformer;
    
    // Whisper models have specific naming
    if (File.Exists(Path.Combine(modelDir, "tiny.en-encoder.onnx")) ||
        Directory.GetFiles(modelDir, "*-encoder.onnx").Length > 0)
        return ModelArchitecture.Whisper;
    
    // CTC models
    if (File.Exists(Path.Combine(modelDir, "ctc.onnx")))
        return ModelArchitecture.ZipformerCtc;
    
    // NeMo models
    if (Directory.GetFiles(modelDir, "*.nemo").Length > 0 ||
        File.Exists(Path.Combine(modelDir, "model.onnx")))
        return ModelArchitecture.NemoFastConformer;
    
    return ModelArchitecture.Unknown;
}
```

#### REST API for Model Management
Endpoints added to AgentHost:

- `GET /api/wyoming/models` — List all available models (catalog + installed status)
- `GET /api/wyoming/models/installed` — List only installed models
- `GET /api/wyoming/models/active` — Get currently active model info
- `POST /api/wyoming/models/{modelId}/download` — Start model download (returns progress token)
- `GET /api/wyoming/models/downloads/{token}` — Check download progress
- `POST /api/wyoming/models/{modelId}/activate` — Switch active model
- `DELETE /api/wyoming/models/{modelId}` — Delete a downloaded model
- `POST /api/wyoming/models/custom` — Register a custom model path

#### Custom Model Support
Users can also point to manually downloaded models:
```json
{
  "Stt": {
    "ActiveModel": "my-custom-model",
    "CustomModels": {
      "my-custom-model": {
        "Path": "/models/stt/my-fine-tuned-model",
        "Type": "streaming-transducer",
        "Languages": ["en"],
        "Description": "My fine-tuned model"
      }
    }
  }
}
```

**Files**:
- `lucia.Wyoming/Models/ModelCatalogService.cs`
- `lucia.Wyoming/Models/ModelDownloader.cs`
- `lucia.Wyoming/Models/ModelManager.cs` (major update)
- `lucia.Wyoming/Models/AsrModelDefinition.cs`
- `lucia.Wyoming/Models/ModelDownloadProgress.cs`
- `lucia.Wyoming/Models/ModelDownloadResult.cs`
- `lucia.Wyoming/Models/ModelFilter.cs`
- `lucia.Wyoming/Models/ModelArchitecture.cs`
- `lucia.AgentHost/Apis/WyomingModelApi.cs`

---

### D9: Aspire Integration

**What**: Expose the in-process Wyoming listener through the existing `lucia.AgentHost` Aspire resource

**Implementation Details**:
- Update `lucia.AppHost/AppHost.cs` to expose a `wyoming-tcp` endpoint on `lucia-agenthost`
- Do not add a separate Wyoming project or Aspire resource
- Remove `WaitFor` dependency because Wyoming runs in-process with AgentHost
- Remove service token injection because Wyoming can call Lucia services directly in-process
- AgentHost and its Wyoming TCP endpoint appear in the Aspire dashboard

**Files**:
- `lucia.AppHost/AppHost.cs` (modify)

---

### D10: Tests

**What**: Unit and integration tests for Phase 1 components

**Test Cases**:

#### Wyoming Protocol Tests
- `WyomingEventParser_ParsesAudioStartEvent`
- `WyomingEventParser_ParsesAudioChunkWithPayload`
- `WyomingEventParser_ParsesDescribeEvent`
- `WyomingEventParser_HandlesInvalidJson`
- `WyomingEventParser_HandlesIncompletePayload`
- `WyomingEventWriter_WritesTranscriptEvent`
- `WyomingEventWriter_WritesInfoEvent`
- `WyomingEventWriter_WritesDetectionEvent`
- `WyomingEvent_RoundTrip_AllEventTypes`

#### Audio Processing Tests
- `AudioBuffer_ProducerConsumer_ThreadSafe`
- `AudioResampler_48kTo16k_CorrectOutput`
- `PcmConverter_Int16ToFloat32_Roundtrip`
- `AudioFormat_ParseFromWyomingData`

#### Integration Tests
- `WyomingServer_AcceptsConnection_RespondsToDescribe`
- `WyomingServer_UnpairedDevice_RejectedAfterDescribe`
- `WyomingServer_SttPipeline_TranscribesAudio`
- `WyomingServer_WakeWord_DetectsKeyword`
- `WyomingServer_ConcurrentConnections_Independent`
- `WyomingServer_DisconnectMidStream_CleansUp`

**Files**:
- `lucia.Tests/Wyoming/WyomingEventParserTests.cs`
- `lucia.Tests/Wyoming/WyomingEventWriterTests.cs`
- `lucia.Tests/Wyoming/AudioBufferTests.cs`
- `lucia.Tests/Wyoming/AudioResamplerTests.cs`
- `lucia.Tests/Wyoming/PcmConverterTests.cs`

---

## Task Breakdown

### Setup Tasks
| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P1-SETUP-001 | Create `lucia.Wyoming` class library | No | Create `lucia.Wyoming` class library, add it to the solution, and wire project references |
| P1-SETUP-002 | Add NuGet dependencies | No | Add sherpa-onnx, Makaretu.Dns.Multicast, NAudio, and related packages to `Directory.Packages.props` |
| P1-SETUP-003 | Integrate AgentHost/AppHost | No | Add `builder.AddWyomingServer()` to AgentHost `Program.cs` and expose the Wyoming TCP port in AppHost |

### Wyoming Protocol Tasks
| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P1-WY-001 | Implement WyomingEvent types | Yes | Define all event types with JSON serialization |
| P1-WY-002 | Implement WyomingEventParser | Yes | Newline-JSON + binary payload reader |
| P1-WY-003 | Implement WyomingEventWriter | Yes | Event serialization + stream writer |
| P1-WY-004 | Implement WyomingSession | No | Per-connection state machine and event dispatch |
| P1-WY-005 | Implement WyomingServer | No | TCP listener + session management hosted service |
| P1-WY-006 | Implement describe/info | No | Service capability advertisement |
| P1-WY-007 | Write protocol tests | Yes | Parser, writer, round-trip tests |

### Device Pairing Tasks
| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P1-PAIR-001 | Implement PairingService | Yes | Store paired device tokens and friendly names in MongoDB for Wyoming session lookups |
| P1-PAIR-002 | Enforce pairing in WyomingSession | No | Allow describe/info, then reject unpaired devices before detect/transcribe |
| P1-PAIR-003 | Add device pairing API endpoints | No | REST API for pair/list/delete used by the dashboard pairing page |

### Audio Processing Tasks
| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P1-AU-001 | Implement AudioFormat | Yes | Format model + Wyoming data parsing |
| P1-AU-002 | Implement PcmConverter | Yes | Int16 ↔ Float32 conversion |
| P1-AU-003 | Implement AudioBuffer | Yes | Thread-safe ring buffer |
| P1-AU-004 | Implement AudioResampler | Yes | Sample rate conversion |
| P1-AU-005 | Implement AudioPipeline | No | VAD → STT orchestration |
| P1-AU-006 | Write audio tests | Yes | Buffer, resampler, converter tests |

### Speech Engine Tasks
| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P1-STT-001 | Implement ISttEngine/ISttSession | Yes | STT abstraction interfaces |
| P1-STT-002 | Implement SherpaSttEngine | No | sherpa-onnx streaming recognizer wrapper |
| P1-STT-003 | Implement IVadEngine | Yes | VAD abstraction interface |
| P1-STT-004 | Implement SherpaVadEngine | No | sherpa-onnx Silero VAD wrapper |
| P1-WW-001 | Implement IWakeWordDetector | Yes | Wake word abstraction interface |
| P1-WW-002 | Implement SherpaWakeWordDetector | No | sherpa-onnx keyword spotter wrapper |
| P1-WW-003 | Implement WakeWordTokenizer | No | Port text2token for in-process BPE tokenization |
| P1-WW-004 | Implement CustomWakeWordManager | No | Register, calibrate, reload custom wake words |
| P1-WW-005 | Implement WakeWordStore | Yes | MongoDB persistence for custom wake word configs |
| P1-WW-006 | Add wake word API endpoints | No | REST API for create/calibrate/list/delete wake words |
| P1-WW-007 | Write custom wake word tests | Yes | Tokenization, calibration auto-tune, reload tests |

### Model Management Tasks
| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P1-MDL-001 | Implement AsrModelDefinition + ModelArchitecture | Yes | Model metadata types and architecture enum |
| P1-MDL-002 | Implement ModelCatalogService | No | Built-in catalog of all sherpa-onnx ASR models |
| P1-MDL-003 | Implement ModelDownloader | No | HTTP download + tar.bz2 extraction |
| P1-MDL-004 | Implement model auto-detection | No | Detect architecture from model directory layout |
| P1-MDL-005 | Implement WyomingModelApi | No | REST endpoints for model browse/download/switch |
| P1-MDL-006 | Write model management tests | Yes | Catalog filter, download, auto-detect tests |

### Infrastructure Tasks
| ID | Task | Parallel | Description |
|----|------|----------|-------------|
| P1-INF-001 | Implement ZeroconfAdvertiser | Yes | mDNS service discovery |
| P1-INF-002 | Implement ModelManager | Yes | Model validation, loading, and runtime switching |
| P1-INF-003 | Add OpenTelemetry instrumentation | No | Spans for STT, wake, VAD |
| P1-INF-004 | Implement health checks | No | Model loaded, TCP listening, memory usage |

---

## Acceptance Criteria

Phase 1 is complete when:
1. ✅ Wyoming TCP listener starts alongside AgentHost and is visible in Aspire dashboard
2. ✅ `describe` event returns server capabilities (STT models, wake words)
3. ✅ Unpaired devices are rejected immediately after the initial `describe`/`info` exchange
4. ✅ Paired devices can be created, listed, and revoked through the dashboard-facing pairing API
5. ✅ Audio sent via Wyoming protocol is transcribed to text via sherpa-onnx
6. ✅ Wake word detection triggers on configured keywords
7. ✅ Service is discoverable via Zeroconf/mDNS
8. ✅ All unit and integration tests pass
9. ✅ OpenTelemetry spans visible in Aspire dashboard
10. ✅ Server handles concurrent connections without crashes
11. ✅ Missing models reported via health check (not silent failure)
12. ✅ Default model works out-of-the-box without user configuration
13. ✅ Users can browse available models via API and download new ones
14. ✅ Model switching works at runtime without server restart
15. ✅ Custom user-provided models can be registered and activated
16. ✅ Custom wake words can be created from text input with zero audio recordings
17. ✅ Optional calibration with 3-5 samples auto-tunes detection sensitivity
18. ✅ Multiple concurrent wake words active simultaneously (one per user + default)
19. ✅ Wake word changes take effect within 5 seconds without server restart
