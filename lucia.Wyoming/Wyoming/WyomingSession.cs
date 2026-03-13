using System.Net.Sockets;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Stt;
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

    private AudioFormat? _currentAudioFormat;
    private ISttSession? _currentSttSession;
    private IWakeWordSession? _currentWakeWordSession;
    private SttResult? _pendingTranscript;
    private bool _disposed;

    public WyomingSession(TcpClient client, IServiceProvider serviceProvider, ILogger<WyomingSession> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _serviceProvider = serviceProvider;
        _logger = logger;
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
        var parser = new WyomingEventParser(stream);
        var writer = new WyomingEventWriter(stream);
        var infoService = new WyomingServiceInfo(services.GetRequiredService<IOptions<WyomingOptions>>());

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
                        HandleDetectEvent(detectEvent, services);
                        break;

                    case AudioStartEvent audioStartEvent:
                        HandleAudioStartEvent(audioStartEvent);
                        break;

                    case AudioChunkEvent audioChunkEvent:
                        await HandleAudioChunkEventAsync(audioChunkEvent, writer, services, ct).ConfigureAwait(false);
                        break;

                    case AudioStopEvent:
                        HandleAudioStopEvent();
                        break;

                    case TranscribeEvent transcribeEvent:
                        await HandleTranscribeEventAsync(transcribeEvent, writer, ct).ConfigureAwait(false);
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

    private void HandleDetectEvent(DetectEvent detectEvent, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(detectEvent);
        ArgumentNullException.ThrowIfNull(services);

        _pendingTranscript = null;
        DisposeCurrentSttSession();

        _currentWakeWordSession?.Dispose();
        _currentWakeWordSession = CreateWakeWordSession(services);
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
                    _currentWakeWordSession = CreateWakeWordSession(services);
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
                _currentSttSession = CreateSttSession(services);
                State = WyomingSessionState.Transcribing;

                await writer.WriteEventAsync(
                        new DetectionEvent
                        {
                            Name = detection.Keyword,
                            Timestamp = detection.Timestamp.ToUnixTimeMilliseconds(),
                        },
                        ct)
                    .ConfigureAwait(false);

                _currentSttSession?.AcceptAudioChunk(samples, _currentAudioFormat.SampleRate);
                break;

            case WyomingSessionState.Transcribing:
                _currentSttSession ??= CreateSttSession(services);
                _currentSttSession?.AcceptAudioChunk(samples, _currentAudioFormat.SampleRate);
                break;
        }
    }

    private void HandleAudioStopEvent()
    {
        switch (State)
        {
            case WyomingSessionState.Transcribing when _currentSttSession is not null:
                _pendingTranscript = _currentSttSession.GetFinalResult();
                DisposeCurrentSttSession();
                State = WyomingSessionState.Processing;
                _logger.LogDebug("Wyoming session {SessionId} finalized STT result", Id);
                break;

            case WyomingSessionState.WakeListening:
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
            _pendingTranscript = _currentSttSession.GetFinalResult();
            DisposeCurrentSttSession();
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

    private ISttSession? CreateSttSession(IServiceProvider services)
    {
        var engine = services.GetServices<ISttEngine>().FirstOrDefault();
        if (engine is null)
        {
            _logger.LogWarning("No STT engine registered for Wyoming session {SessionId}", Id);
            return null;
        }

        return engine.CreateSession();
    }

    private IWakeWordSession? CreateWakeWordSession(IServiceProvider services)
    {
        var detector = services.GetServices<IWakeWordDetector>().FirstOrDefault(static item => item.IsReady);
        if (detector is null)
        {
            _logger.LogWarning("No ready wake word detector registered for Wyoming session {SessionId}", Id);
            return null;
        }

        return detector.CreateSession();
    }

    private void DisposeEngineSessions()
    {
        DisposeCurrentSttSession();

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
