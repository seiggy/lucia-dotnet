using System.Net.Sockets;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Wyoming;

public sealed class WyomingSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WyomingSession> _logger;
    private readonly WyomingOptions _options;

    private AudioFormat? _currentAudioFormat;
    private ISttSession? _currentSttSession;
    private IVadSession? _currentVadSession;
    private IWakeWordSession? _currentWakeWordSession;
    private SttResult? _pendingTranscript;
    private bool _disposed;

    public WyomingSession(
        TcpClient client,
        IServiceProvider serviceProvider,
        ILogger<WyomingSession> logger,
        WyomingOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
        Id = Guid.NewGuid().ToString("N");
        State = WyomingSessionState.Connected;
    }

    public string Id { get; }

    public WyomingSessionState State { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var stream = _client.GetStream();
        var parser = new WyomingEventParser(stream, _options);
        var writer = new WyomingEventWriter(stream);
        var sttEngine = services.GetServices<ISttEngine>().FirstOrDefault();
        var wakeWordDetector = services.GetServices<IWakeWordDetector>().FirstOrDefault();
        var infoService = new WyomingServiceInfo(
            services.GetRequiredService<IOptions<WyomingOptions>>(),
            sttEngine,
            wakeWordDetector);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var evt = await parser.ReadEventAsync(ct).ConfigureAwait(false);
                if (evt is null)
                {
                    _logger.LogDebug("Wyoming client disconnected for session {SessionId}", Id);
                    State = WyomingSessionState.Disconnected;
                    break;
                }

                switch (evt)
                {
                    case DescribeEvent:
                        await writer.WriteEventAsync(infoService.BuildInfoEvent(), ct).ConfigureAwait(false);
                        break;

                    case DetectEvent detectEvent:
                        await HandleDetectEventAsync(detectEvent, writer, services, ct).ConfigureAwait(false);
                        break;

                    case AudioStartEvent audioStartEvent:
                        HandleAudioStartEvent(audioStartEvent);
                        break;

                    case AudioChunkEvent audioChunkEvent:
                        await HandleAudioChunkEventAsync(audioChunkEvent, writer, services, ct).ConfigureAwait(false);
                        break;

                    case AudioStopEvent:
                        await HandleAudioStopEventAsync(writer, ct).ConfigureAwait(false);
                        break;

                    case TranscribeEvent transcribeEvent:
                        await HandleTranscribeEventAsync(transcribeEvent, writer, ct).ConfigureAwait(false);
                        break;

                    case SynthesizeEvent:
                        _logger.LogWarning("TTS synthesis not yet implemented (Phase 3)");
                        await writer.WriteEventAsync(
                                new ErrorEvent
                                {
                                    Text = "Text-to-speech is not yet available",
                                    Code = "tts_not_implemented",
                                },
                                ct)
                            .ConfigureAwait(false);
                        break;

                    default:
                        _logger.LogDebug(
                            "Ignoring unsupported Wyoming event {EventType} for session {SessionId}",
                            evt.Type,
                            Id);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            State = WyomingSessionState.Disconnected;
            _logger.LogDebug("Wyoming session {SessionId} cancelled", Id);
        }
        catch (IOException ex)
        {
            State = WyomingSessionState.Disconnected;
            _pendingTranscript = null;
            _logger.LogInformation(ex, "I/O ended Wyoming session {SessionId}", Id);
        }
        catch (WyomingProtocolException ex)
        {
            _logger.LogWarning(ex, "Protocol error in Wyoming session {SessionId}", Id);
            await TryWriteErrorAsync(writer, ex.Message, "protocol_error", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in Wyoming session {SessionId}", Id);
            await TryWriteErrorAsync(writer, "Internal Wyoming session error.", "internal_error", ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            State = WyomingSessionState.Disconnected;
            DisposeEngineSessions();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        State = WyomingSessionState.Disconnected;
        DisposeEngineSessions();
        _client.Dispose();
    }

    private async Task HandleDetectEventAsync(
        DetectEvent detectEvent,
        WyomingEventWriter writer,
        IServiceProvider services,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(detectEvent);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(services);

        _pendingTranscript = null;
        DisposeCurrentSttSession();
        DisposeCurrentVadSession();

        _currentWakeWordSession?.Dispose();
        _currentWakeWordSession = await CreateWakeWordSessionAsync(services, writer, ct).ConfigureAwait(false);
        if (_currentWakeWordSession is null)
        {
            return;
        }

        State = WyomingSessionState.WakeListening;

        _logger.LogInformation(
            "Wyoming session {SessionId} entered wake listening state{Filter}",
            Id,
            detectEvent.Names is { Length: > 0 }
                ? $" with filters [{string.Join(", ", detectEvent.Names)}]"
                : string.Empty);
    }

    private void HandleAudioStartEvent(AudioStartEvent audioStartEvent)
    {
        ArgumentNullException.ThrowIfNull(audioStartEvent);

        _currentAudioFormat = new AudioFormat
        {
            SampleRate = audioStartEvent.Rate,
            BitsPerSample = audioStartEvent.Width * 8,
            Channels = audioStartEvent.Channels,
        };

        _logger.LogDebug(
            "Wyoming session {SessionId} started audio stream {Rate}Hz/{Bits}bit/{Channels}ch",
            Id,
            _currentAudioFormat.SampleRate,
            _currentAudioFormat.BitsPerSample,
            _currentAudioFormat.Channels);
    }

    private async Task HandleAudioChunkEventAsync(
        AudioChunkEvent audioChunkEvent,
        WyomingEventWriter writer,
        IServiceProvider services,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(audioChunkEvent);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(services);

        if (audioChunkEvent.Payload is not { Length: > 0 })
        {
            return;
        }

        _currentAudioFormat ??= new AudioFormat
        {
            SampleRate = audioChunkEvent.Rate,
            BitsPerSample = audioChunkEvent.Width * 8,
            Channels = audioChunkEvent.Channels,
        };

        var samples = ConvertAudioChunkToMonoSamples(audioChunkEvent.Payload, _currentAudioFormat);
        if (samples.Length == 0)
        {
            return;
        }

        switch (State)
        {
            case WyomingSessionState.WakeListening:
                if (_currentWakeWordSession is null)
                {
                    _currentWakeWordSession = await CreateWakeWordSessionAsync(services, writer, ct).ConfigureAwait(false);
                }

                if (_currentWakeWordSession is null)
                {
                    return;
                }

                _currentWakeWordSession.AcceptAudioChunk(samples, _currentAudioFormat.SampleRate);
                var detection = _currentWakeWordSession.CheckForDetection();
                if (detection is null)
                {
                    return;
                }

                _logger.LogInformation(
                    "Wake word {Keyword} detected for session {SessionId} with confidence {Confidence}",
                    detection.Keyword,
                    Id,
                    detection.Confidence);

                _pendingTranscript = null;
                _currentWakeWordSession.Reset();
                _currentSttSession = await CreateSttSessionAsync(services, writer, ct).ConfigureAwait(false);
                if (_currentSttSession is null)
                {
                    return;
                }

                _currentVadSession = await CreateVadSessionAsync(services, writer, ct).ConfigureAwait(false);
                if (_currentVadSession is null)
                {
                    DisposeCurrentSttSession();
                    return;
                }

                State = WyomingSessionState.Transcribing;

                await writer.WriteEventAsync(
                        new DetectionEvent
                        {
                            Name = detection.Keyword,
                            Timestamp = detection.Timestamp.ToUnixTimeMilliseconds(),
                        },
                        ct)
                    .ConfigureAwait(false);

                ProcessSpeechSamples(samples);
                break;

            case WyomingSessionState.Transcribing:
                _currentSttSession ??=
                    await CreateSttSessionAsync(services, writer, ct).ConfigureAwait(false);
                _currentVadSession ??=
                    await CreateVadSessionAsync(services, writer, ct).ConfigureAwait(false);

                if (_currentSttSession is null || _currentVadSession is null)
                {
                    DisposeCurrentSttSession();
                    DisposeCurrentVadSession();
                    return;
                }

                ProcessSpeechSamples(samples);
                break;
        }
    }

    private async Task HandleAudioStopEventAsync(WyomingEventWriter writer, CancellationToken ct)
    {
        switch (State)
        {
            case WyomingSessionState.Transcribing when _currentSttSession is not null:
                _currentVadSession?.Flush();
                DrainVadSegmentsToStt();
                _pendingTranscript = _currentSttSession.GetFinalResult();
                DisposeCurrentSttSession();
                DisposeCurrentVadSession();
                State = WyomingSessionState.Processing;
                _logger.LogDebug("Wyoming session {SessionId} finalized STT result", Id);
                break;

            case WyomingSessionState.WakeListening:
                _logger.LogDebug("Audio stream ended during wake word listening without detection");
                await writer.WriteEventAsync(new NotDetectedEvent(), ct).ConfigureAwait(false);
                _currentWakeWordSession?.Dispose();
                _currentWakeWordSession = null;
                State = WyomingSessionState.Connected;
                break;
        }

        _currentAudioFormat = null;
    }

    private async Task HandleTranscribeEventAsync(
        TranscribeEvent transcribeEvent,
        WyomingEventWriter writer,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(transcribeEvent);
        ArgumentNullException.ThrowIfNull(writer);

        if (_pendingTranscript is null && _currentSttSession is not null)
        {
            _currentVadSession?.Flush();
            DrainVadSegmentsToStt();
            _pendingTranscript = _currentSttSession.GetFinalResult();
            DisposeCurrentSttSession();
            DisposeCurrentVadSession();
        }

        var transcript = _pendingTranscript ?? new SttResult();
        State = WyomingSessionState.Responding;

        await writer.WriteEventAsync(
                new TranscriptEvent
                {
                    Text = transcript.Text,
                    Confidence = transcript.Confidence,
                },
                ct)
            .ConfigureAwait(false);

        _pendingTranscript = null;
        State = WyomingSessionState.Connected;
    }

    private async Task<ISttSession?> CreateSttSessionAsync(
        IServiceProvider services,
        WyomingEventWriter writer,
        CancellationToken ct)
    {
        var engines = services.GetServices<ISttEngine>().ToArray();
        var engine = engines.FirstOrDefault(static item => IsSttEngineReady(item))
            ?? engines.FirstOrDefault();
        if (engine is null)
        {
            _logger.LogWarning("No STT engine registered for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Speech recognition is not available. STT model may not be installed.",
                    "stt_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }

        if (!IsSttEngineReady(engine))
        {
            _logger.LogWarning("STT engine not ready for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Speech recognition is not available. STT model may not be installed.",
                    "stt_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }

        try
        {
            return engine.CreateSession();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "STT engine failed to create session for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Speech recognition is not available. STT model may not be installed.",
                    "stt_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }
    }

    private async Task<IVadSession?> CreateVadSessionAsync(
        IServiceProvider services,
        WyomingEventWriter writer,
        CancellationToken ct)
    {
        var engines = services.GetServices<IVadEngine>().ToArray();
        var engine = engines.FirstOrDefault(static item => item.IsReady)
            ?? engines.FirstOrDefault();
        if (engine is null)
        {
            _logger.LogWarning("No VAD engine registered for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Voice activity detection is not available. VAD model may not be installed.",
                    "vad_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }

        if (!engine.IsReady)
        {
            _logger.LogWarning("VAD engine not ready for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Voice activity detection is not available. VAD model may not be installed.",
                    "vad_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }

        try
        {
            return engine.CreateSession();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "VAD engine failed to create session for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Voice activity detection is not available. VAD model may not be installed.",
                    "vad_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }
    }

    private async Task<IWakeWordSession?> CreateWakeWordSessionAsync(
        IServiceProvider services,
        WyomingEventWriter writer,
        CancellationToken ct)
    {
        var detectors = services.GetServices<IWakeWordDetector>().ToArray();
        var detector = detectors.FirstOrDefault(static item => item.IsReady)
            ?? detectors.FirstOrDefault();
        if (detector is null)
        {
            _logger.LogWarning("No ready wake word detector registered for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Wake word detection is not available. Wake word model may not be installed.",
                    "wake_word_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }

        if (!detector.IsReady)
        {
            _logger.LogWarning("Wake word detector not ready for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Wake word detection is not available. Wake word model may not be installed.",
                    "wake_word_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }

        try
        {
            return detector.CreateSession();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "Wake word detector failed to create session for Wyoming session {SessionId}", Id);
            await ReportUnavailableAsync(
                    writer,
                    "Wake word detection is not available. Wake word model may not be installed.",
                    "wake_word_unavailable",
                    ct)
                .ConfigureAwait(false);
            return null;
        }
    }

    private void DisposeEngineSessions()
    {
        DisposeCurrentSttSession();
        DisposeCurrentVadSession();

        _currentWakeWordSession?.Dispose();
        _currentWakeWordSession = null;

        _pendingTranscript = null;
        _currentAudioFormat = null;
    }

    private void DisposeCurrentSttSession()
    {
        _currentSttSession?.Dispose();
        _currentSttSession = null;
    }

    private void DisposeCurrentVadSession()
    {
        _currentVadSession?.Dispose();
        _currentVadSession = null;
    }

    private async Task ReportUnavailableAsync(
        WyomingEventWriter writer,
        string message,
        string code,
        CancellationToken ct)
    {
        await TryWriteErrorAsync(writer, message, code, ct).ConfigureAwait(false);
        DisposeEngineSessions();
        if (State != WyomingSessionState.Disconnected)
        {
            State = WyomingSessionState.Connected;
        }
    }

    private static bool IsSttEngineReady(ISttEngine engine) =>
        engine switch
        {
            SherpaSttEngine sherpaEngine => sherpaEngine.IsReady,
            _ => true,
        };

    private void ProcessSpeechSamples(ReadOnlySpan<float> samples)
    {
        if (_currentSttSession is null || _currentVadSession is null)
        {
            return;
        }

        _currentVadSession.AcceptAudioChunk(samples);
        DrainVadSegmentsToStt();
    }

    private void DrainVadSegmentsToStt()
    {
        if (_currentSttSession is null || _currentVadSession is null)
        {
            return;
        }

        while (_currentVadSession.HasSpeechSegment)
        {
            var segment = _currentVadSession.GetNextSegment();
            _currentSttSession.AcceptAudioChunk(segment.Samples, segment.SampleRate);
        }
    }

    private static float[] ConvertAudioChunkToMonoSamples(byte[] payload, AudioFormat audioFormat)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(audioFormat);

        if (audioFormat.BitsPerSample != 16)
        {
            throw new WyomingProtocolException($"Unsupported PCM width: {audioFormat.BitsPerSample} bits.");
        }

        var samples = PcmConverter.Int16ToFloat32(payload);
        if (audioFormat.Channels <= 1)
        {
            return samples;
        }

        var frameCount = samples.Length / audioFormat.Channels;
        if (frameCount == 0)
        {
            return [];
        }

        var monoSamples = new float[frameCount];
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var sum = 0f;
            var frameOffset = frameIndex * audioFormat.Channels;
            for (var channelIndex = 0; channelIndex < audioFormat.Channels; channelIndex++)
            {
                sum += samples[frameOffset + channelIndex];
            }

            monoSamples[frameIndex] = sum / audioFormat.Channels;
        }

        return monoSamples;
    }

    private async Task TryWriteErrorAsync(
        WyomingEventWriter writer,
        string message,
        string code,
        CancellationToken ct)
    {
        try
        {
            if (_client.Connected)
            {
                await writer.WriteEventAsync(new ErrorEvent { Text = message, Code = code }, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            State = WyomingSessionState.Disconnected;
            _client.Dispose();
            _logger.LogDebug(ex, "Failed to send Wyoming error event for session {SessionId}", Id);
        }
    }
}
