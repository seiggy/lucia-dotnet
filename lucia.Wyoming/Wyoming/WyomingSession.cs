using System.Diagnostics;
using System.Net.Sockets;
using lucia.Wyoming.Audio;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Telemetry;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Wyoming;

public sealed class WyomingSession : IDisposable
{
    private static readonly ActivitySource WyomingActivitySource = new("lucia.Wyoming.Session", "1.0.0");
    private readonly TcpClient _client;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WyomingSession> _logger;
    private readonly WyomingOptions _options;

    private AudioFormat? _currentAudioFormat;
    private ISttSession? _currentSttSession;
    private IVadSession? _currentVadSession;
    private IWakeWordSession? _currentWakeWordSession;
    private SttResult? _pendingTranscript;
    private readonly List<float> _utteranceAudioBuffer = [];
    private readonly List<float> _rawUtteranceAudioBuffer = [];
    private int _utteranceSampleRate = 16_000;
    private IDiarizationEngine? _diarizationEngine;
    private ISpeakerProfileStore? _profileStore;
    private IOptionsMonitor<VoiceProfileOptions>? _voiceProfileOptions;
    private SpeakerVerificationFilter? _speakerFilter;
    private UnknownSpeakerTracker? _unknownTracker;
    private AdaptiveProfileUpdater? _adaptiveUpdater;
    private AudioClipService? _audioClipService;
    private ICommandRouter? _commandRouter;
    private SkillDispatcher? _skillDispatcher;
    private ITranscriptStore? _transcriptStore;
    private ModelManager? _modelManager;
    private ISpeechEnhancer? _speechEnhancer;
    private ISpeechEnhancerSession? _currentEnhancerSession;
    private readonly SessionEventBus? _eventBus;
    private DateTimeOffset _lastAudioLevelEvent;
    private long _enhancementTotalMs;
    private long _sttFinalizationMs;
    private long _diarizationMs;
    private long _audioStreamStartedAt;
    private bool _disposed;

    public WyomingSession(
        TcpClient client,
        IServiceProvider serviceProvider,
        ILogger<WyomingSession> logger,
        WyomingOptions options,
        SessionEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
        _eventBus = eventBus;
        Id = Guid.NewGuid().ToString("N");
        State = WyomingSessionState.Connected;
    }

    public string Id { get; }

    public WyomingSessionState State { get; private set; }

    private void SetState(WyomingSessionState newState)
    {
        State = newState;
        _eventBus?.Publish(new SessionStateChangedEvent { SessionId = Id, State = newState });
    }

    private void TryPublishAudioLevel(ReadOnlySpan<float> samples, bool isSpeechActive)
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastAudioLevelEvent).TotalMilliseconds < 250) return;
        _lastAudioLevelEvent = now;

        var sumSquares = 0f;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }

        var rms = samples.Length > 0 ? MathF.Sqrt(sumSquares / samples.Length) : 0f;
        _eventBus?.Publish(new AudioLevelEvent
        {
            SessionId = Id,
            RmsLevel = rms,
            ActiveVoiceCount = isSpeechActive ? 1 : 0,
        });
    }

    public async Task RunAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var sessionActivity = WyomingActivitySource.StartActivity("wyoming.session");
        sessionActivity?.SetTag("session.id", Id);

        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        ResolveOptionalServices(services);
        var stream = _client.GetStream();
        var parser = new WyomingEventParser(stream, _options);
        var writer = new WyomingEventWriter(stream);
        var sttEngines = services.GetServices<ISttEngine>();
        var wakeWordDetector = services.GetServices<IWakeWordDetector>().FirstOrDefault();
        var infoService = new WyomingServiceInfo(
            services.GetRequiredService<IOptions<WyomingOptions>>(),
            sttEngines,
            wakeWordDetector);

        _logger.LogInformation("Session {SessionId} started", Id);
        var sessionSw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var evt = await parser.ReadEventAsync(ct).ConfigureAwait(false);
                if (evt is null)
                {
                    _logger.LogDebug("Wyoming client disconnected for session {SessionId}", Id);
                    SetState(WyomingSessionState.Disconnected);
                    break;
                }

                switch (evt)
                {
                    case DescribeEvent:
                        using (WyomingActivitySource.StartActivity("wyoming.describe"))
                        {
                            await writer.WriteEventAsync(infoService.BuildInfoEvent(), ct).ConfigureAwait(false);
                        }
                        break;

                    case DetectEvent detectEvent:
                        await HandleDetectEventAsync(detectEvent, writer, services, ct).ConfigureAwait(false);
                        break;

                    case AudioStartEvent audioStartEvent:
                        _audioStreamStartedAt = sessionSw.ElapsedMilliseconds;
                        _logger.LogInformation(
                            "Session {SessionId} audio stream started at {ElapsedMs}ms",
                            Id, _audioStreamStartedAt);
                        HandleAudioStartEvent(audioStartEvent);
                        break;

                    case AudioChunkEvent audioChunkEvent:
                        await HandleAudioChunkEventAsync(audioChunkEvent, writer, services, ct).ConfigureAwait(false);
                        break;

                    case AudioStopEvent:
                        _logger.LogInformation(
                            "Session {SessionId} audio-stop received at {ElapsedMs}ms ({AudioMs}ms since stream start)",
                            Id, sessionSw.ElapsedMilliseconds,
                            sessionSw.ElapsedMilliseconds - _audioStreamStartedAt);
                        await HandleAudioStopEventAsync(writer, ct).ConfigureAwait(false);
                        _logger.LogInformation(
                            "Session {SessionId} audio-stop processing complete at {ElapsedMs}ms",
                            Id, sessionSw.ElapsedMilliseconds);
                        break;

                    case TranscribeEvent transcribeEvent:
                        _logger.LogInformation(
                            "Session {SessionId} transcribe event at {ElapsedMs}ms",
                            Id, sessionSw.ElapsedMilliseconds);
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
            SetState(WyomingSessionState.Disconnected);
            _logger.LogDebug("Wyoming session {SessionId} cancelled", Id);
        }
        catch (IOException ex)
        {
            SetState(WyomingSessionState.Disconnected);
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
            SetState(WyomingSessionState.Disconnected);
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
        ResetUtteranceAudio();
        DisposeCurrentSttSession();
        DisposeCurrentVadSession();

        _currentWakeWordSession?.Dispose();
        _currentWakeWordSession = await CreateWakeWordSessionAsync(services, writer, ct).ConfigureAwait(false);
        if (_currentWakeWordSession is null)
        {
            return;
        }

        SetState(WyomingSessionState.WakeListening);

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
            case WyomingSessionState.Connected:
                // Direct STT flow: HA sends audio without a Detect event (wake word
                // handled on-device or by HA). Transition straight to Transcribing.
                _pendingTranscript = null;
                _lastPartialText = string.Empty;
                ResetUtteranceAudio();
                _currentSttSession = await CreateSttSessionAsync(services, writer, ct).ConfigureAwait(false);
                if (_currentSttSession is null)
                {
                    return;
                }

                _currentVadSession = await CreateVadSessionAsync(services, writer, ct).ConfigureAwait(false);
                if (_currentVadSession is null)
                {
                    _logger.LogWarning("VAD unavailable for session {SessionId} — processing STT without voice activity detection", Id);
                }

                TryCreateEnhancerSession();
                SetState(WyomingSessionState.Transcribing);
                ProcessSpeechSamples(samples);
                break;

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
                // VAD is optional — continue without it

                TryCreateEnhancerSession();
                SetState(WyomingSessionState.Transcribing);

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

                if (_currentSttSession is null)
                {
                    DisposeCurrentSttSession();
                    DisposeCurrentVadSession();
                    return;
                }

                TryCreateEnhancerSession();
                ProcessSpeechSamples(samples);
                break;
        }
    }

    private async Task HandleAudioStopEventAsync(WyomingEventWriter writer, CancellationToken ct)
    {
        switch (State)
        {
            case WyomingSessionState.Transcribing when _currentSttSession is not null:
                using (var audioStopActivity = WyomingActivitySource.StartActivity("wyoming.audio_stop"))
                {
                    audioStopActivity?.SetTag("session.id", Id);

                    _currentVadSession?.Flush();

                    SttResult sttResult;
                    using (var sttActivity = WyomingActivitySource.StartActivity("wyoming.stt.finalize"))
                    {
                        var sttSw = System.Diagnostics.Stopwatch.StartNew();
                        sttResult = await _currentSttSession.GetFinalResultAsync().ConfigureAwait(false);
                        sttSw.Stop();
                        _sttFinalizationMs = sttSw.ElapsedMilliseconds;
                        sttActivity?.SetTag("stt.duration_ms", sttSw.ElapsedMilliseconds);
                        sttActivity?.SetTag("stt.text", sttResult.Text);
                        _logger.LogInformation(
                            "Session {SessionId} STT finalized in {SttMs}ms → \"{Text}\"",
                            Id, sttSw.ElapsedMilliseconds, sttResult.Text);
                    }

                    _pendingTranscript = sttResult;
                    DisposeCurrentSttSession();
                    DisposeCurrentVadSession();
                    SetState(WyomingSessionState.Processing);

                    var transcript = _pendingTranscript ?? new SttResult();
                    if (!string.IsNullOrWhiteSpace(transcript.Text))
                    {
                        _eventBus?.Publish(new SessionTranscriptEvent
                        {
                            SessionId = Id,
                            Text = transcript.Text,
                            Confidence = transcript.Confidence,
                            IsFinal = true,
                        });
                    }

                    var utteranceAudio = _utteranceAudioBuffer.Count == 0
                        ? Array.Empty<float>()
                        : [.. _utteranceAudioBuffer];

                    SetState(WyomingSessionState.Responding);

                    using (WyomingActivitySource.StartActivity("wyoming.process_transcript"))
                    {
                        await ProcessTranscriptAsync(
                                transcript.Text, transcript.Confidence, utteranceAudio, writer, ct)
                            .ConfigureAwait(false);
                    }

                    _pendingTranscript = null;
                    ResetUtteranceAudio();
                    SetState(WyomingSessionState.Connected);
                }
                break;

            case WyomingSessionState.WakeListening:
                _logger.LogDebug("Audio stream ended during wake word listening without detection");
                await writer.WriteEventAsync(new NotDetectedEvent(), ct).ConfigureAwait(false);
                _currentWakeWordSession?.Dispose();
                _currentWakeWordSession = null;
                SetState(WyomingSessionState.Connected);
                break;

            case WyomingSessionState.Connected when _currentAudioFormat is not null:
                // AudioStop received but no audio chunks arrived — send empty transcript
                _logger.LogDebug("Audio stream ended without any audio data for session {SessionId}", Id);
                await writer.WriteEventAsync(
                    new TranscriptEvent { Text = string.Empty, Confidence = 0f }, ct)
                    .ConfigureAwait(false);
                SetState(WyomingSessionState.Connected);
                break;
        }

        _currentAudioFormat = null;
    }

    private Task HandleTranscribeEventAsync(
        TranscribeEvent transcribeEvent,
        WyomingEventWriter writer,
        CancellationToken ct)
    {
        // Per Wyoming protocol, 'transcribe' sets language/model preferences
        // for the upcoming audio stream. The actual transcript is sent after audio-stop.
        // If a transcript is already pending (audio already finalized), send it now.
        // Otherwise, just store the preferences and wait for audio.
        if (State == WyomingSessionState.Processing || _pendingTranscript is not null)
        {
            return SendPendingTranscriptAsync(writer, ct);
        }

        // Store language preference for the current/upcoming session
        _logger.LogDebug(
            "Wyoming session {SessionId} received transcribe event (language={Language}, name={Name})",
            Id, transcribeEvent.Language, transcribeEvent.Name);

        return Task.CompletedTask;
    }

    private async Task SendPendingTranscriptAsync(WyomingEventWriter writer, CancellationToken ct)
    {
        if (_pendingTranscript is null && _currentSttSession is not null)
        {
            _currentVadSession?.Flush();
            _pendingTranscript = await _currentSttSession.GetFinalResultAsync().ConfigureAwait(false);
            DisposeCurrentSttSession();
            DisposeCurrentVadSession();
        }

        var transcript = _pendingTranscript ?? new SttResult();
        var utteranceAudio = _utteranceAudioBuffer.Count == 0
            ? Array.Empty<float>()
            : [.. _utteranceAudioBuffer];
        SetState(WyomingSessionState.Responding);

        await ProcessTranscriptAsync(transcript.Text, transcript.Confidence, utteranceAudio, writer, ct)
            .ConfigureAwait(false);

        _pendingTranscript = null;
        ResetUtteranceAudio();
        SetState(WyomingSessionState.Connected);
    }

    private async Task<ISttSession?> CreateSttSessionAsync(
        IServiceProvider services,
        WyomingEventWriter writer,
        CancellationToken ct)
    {
        var engines = services.GetServices<ISttEngine>().ToArray();
        var manager = services.GetService<ModelManager>();
        var preferStreaming = manager?.PreferredSttEngineType == EngineType.Stt;

        // Prefer the engine type the user last activated
        var engine = preferStreaming
            ? (engines.OfType<SherpaSttEngine>().FirstOrDefault(static e => IsSttEngineReady(e)) as ISttEngine
                ?? engines.FirstOrDefault(static e => IsSttEngineReady(e)))
            : (engines.OfType<HybridSttEngine>().FirstOrDefault(static e => IsSttEngineReady(e)) as ISttEngine
                ?? engines.FirstOrDefault(static e => IsSttEngineReady(e)));
        engine ??= engines.FirstOrDefault();

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
        if (engine is null || !engine.IsReady)
        {
            // VAD is optional — STT works without it, just no speech segmentation
            _logger.LogDebug("VAD engine not available for session {SessionId}", Id);
            return null;
        }

        try
        {
            return engine.CreateSession();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VAD engine failed to create session for Wyoming session {SessionId}", Id);
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

        _currentEnhancerSession?.Dispose();
        _currentEnhancerSession = null;

        _currentWakeWordSession?.Dispose();
        _currentWakeWordSession = null;

        _pendingTranscript = null;
        _currentAudioFormat = null;
        ResetUtteranceAudio();
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

    private void TryCreateEnhancerSession()
    {
        if (_currentEnhancerSession is not null || _speechEnhancer is not { IsReady: true })
        {
            return;
        }

        try
        {
            _currentEnhancerSession = _speechEnhancer.CreateSession();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create speech enhancement session for {SessionId}", Id);
        }
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
            SetState(WyomingSessionState.Connected);
        }
    }

    private static bool IsSttEngineReady(ISttEngine engine) =>
        engine switch
        {
            SherpaSttEngine sherpaEngine => sherpaEngine.IsReady,
            _ => true,
        };

    private string _lastPartialText = string.Empty;
    private DateTimeOffset _lastPartialPublish;

    private void ProcessSpeechSamples(ReadOnlySpan<float> samples)
    {
        if (_currentSttSession is null)
        {
            return;
        }

        // Per-frame streaming speech enhancement (GTCRN via ISpeechEnhancerSession).
        // Enhancement feeds improved audio to the utterance buffer for diarization/storage.
        // The hybrid STT session receives RAW audio — Parakeet is robust to noise and
        // GTCRN's overlap-add lag can cause buffer discontinuities that confuse re-transcription.
        ReadOnlySpan<float> processedSamples = samples;
        float[]? enhancedBuffer = null;
        if (_currentEnhancerSession is not null)
        {
            try
            {
                var enhSw = System.Diagnostics.Stopwatch.StartNew();
                enhancedBuffer = _currentEnhancerSession.Process(samples.ToArray());
                enhSw.Stop();
                _enhancementTotalMs += enhSw.ElapsedMilliseconds;
                if (enhancedBuffer.Length > 0)
                {
                    // Store enhanced audio for clip storage
                    AppendUtteranceAudio(enhancedBuffer, _utteranceSampleRate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Enhancement frame processing failed for session {SessionId}, using raw audio", Id);
            }

            // Always accumulate raw audio for speaker verification — speech
            // enhancement alters spectral characteristics the embedding model
            // relies on, but enrollment uses raw audio.
            AppendRawUtteranceAudio(samples, _utteranceSampleRate);
        }
        else
        {
            AppendUtteranceAudio(samples, _utteranceSampleRate);
        }

        // Always feed raw audio to STT — the hybrid engine re-transcribes the full
        // buffer each cycle, and raw audio is more consistent than GTCRN-enhanced chunks.
        _currentSttSession.AcceptAudioChunk(samples, _utteranceSampleRate);

        if (_currentVadSession is not null)
        {
            _currentVadSession.AcceptAudioChunk(samples);
            TryPublishAudioLevel(samples, _currentVadSession.HasSpeechSegment);
        }
        else
        {
            TryPublishAudioLevel(processedSamples, isSpeechActive: true);
        }

        TryPublishPartialTranscript();
    }

    private void TryPublishPartialTranscript()
    {
        if (_currentSttSession is null || _eventBus is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if ((now - _lastPartialPublish).TotalMilliseconds < 300)
        {
            return;
        }

        _lastPartialPublish = now;

        try
        {
            var partial = _currentSttSession.GetPartialResult();
            if (string.IsNullOrWhiteSpace(partial.Text) || partial.Text == _lastPartialText)
            {
                return;
            }

            _lastPartialText = partial.Text;
            _eventBus.Publish(new SessionTranscriptEvent
            {
                SessionId = Id,
                Text = partial.Text,
                Confidence = partial.Confidence,
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get partial STT result for session {SessionId}", Id);
        }
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
            AppendUtteranceAudio(segment.Samples, segment.SampleRate);
            _currentSttSession.AcceptAudioChunk(segment.Samples, segment.SampleRate);
        }
    }

    private void ResolveOptionalServices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _diarizationEngine = services.GetService<IDiarizationEngine>();
        _profileStore = services.GetService<ISpeakerProfileStore>();
        _voiceProfileOptions = services.GetService<IOptionsMonitor<VoiceProfileOptions>>();
        _speakerFilter = services.GetService<SpeakerVerificationFilter>();
        _unknownTracker = services.GetService<UnknownSpeakerTracker>();
        _adaptiveUpdater = services.GetService<AdaptiveProfileUpdater>();
        _audioClipService = services.GetService<AudioClipService>();
        _commandRouter = services.GetService<ICommandRouter>();
        _skillDispatcher = services.GetService<SkillDispatcher>();
        _transcriptStore = services.GetService<ITranscriptStore>();
        _modelManager = services.GetService<ModelManager>();
        _speechEnhancer = services.GetService<ISpeechEnhancer>();
    }

    private async Task ProcessTranscriptAsync(
        string transcript,
        float originalConfidence,
        float[] utteranceAudio,
        WyomingEventWriter writer,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(transcript);

        if (string.IsNullOrWhiteSpace(transcript))
        {
            await writer.WriteEventAsync(
                    new TranscriptEvent { Text = transcript, Confidence = originalConfidence },
                    ct)
                .ConfigureAwait(false);
            return;
        }

        // Identify speaker
        SpeakerIdentification? speaker;
        using (var diarActivity = WyomingActivitySource.StartActivity("wyoming.diarization"))
        {
            var diarSw = System.Diagnostics.Stopwatch.StartNew();
            speaker = await IdentifySpeakerAsync(utteranceAudio, transcript, ct)
                .ConfigureAwait(false);
            diarSw.Stop();
            _diarizationMs = diarSw.ElapsedMilliseconds;
            diarActivity?.SetTag("diarization.duration_ms", diarSw.ElapsedMilliseconds);
            diarActivity?.SetTag("diarization.speaker", speaker?.Name ?? "unknown");
            _logger.LogInformation(
                "Session {SessionId} diarization completed in {DiarizationMs}ms → {Speaker}",
                Id, diarSw.ElapsedMilliseconds, speaker?.Name ?? "unknown");
        }

        var speakerTag = FormatSpeakerTag(speaker);
        var taggedTranscript = $"{speakerTag}{transcript}";

        // Send transcript to HA via Wyoming protocol
        using (var writeActivity = WyomingActivitySource.StartActivity("wyoming.send_transcript"))
        {
            var writeSw = System.Diagnostics.Stopwatch.StartNew();
            await writer.WriteEventAsync(
                    new TranscriptEvent
                    {
                        Text = taggedTranscript,
                        Confidence = originalConfidence,
                    },
                    ct)
                .ConfigureAwait(false);
            writeSw.Stop();
            writeActivity?.SetTag("transcript.text", taggedTranscript);
            writeActivity?.SetTag("transcript.write_ms", writeSw.ElapsedMilliseconds);
            _logger.LogInformation(
                "Session {SessionId} sent transcript in {WriteMs}ms: {Transcript}",
                Id, writeSw.ElapsedMilliseconds, taggedTranscript);
        }

        // Publish to dashboard
        _eventBus?.Publish(new SessionTranscriptEvent
        {
            SessionId = Id,
            Text = transcript,
            Confidence = originalConfidence,
            SpeakerId = speaker?.ProfileId,
            SpeakerName = speaker?.Name,
            IsFinal = true,
        });

        // Storage is fire-and-forget — don't block the connection teardown
        _ = Task.Run(() => TrySaveTranscriptRecordAsync(
            transcript, originalConfidence, utteranceAudio,
            speaker, route: null, responseText: taggedTranscript,
            commandFiltered: false, CancellationToken.None), CancellationToken.None);
    }

    /// <summary>
    /// Formats the speaker identification as an XML tag prefix.
    /// Always includes a tag, defaulting to &lt;Unknown1 /&gt; when diarization is unavailable.
    /// </summary>
    private static string FormatSpeakerTag(SpeakerIdentification? speaker)
    {
        if (speaker is null || string.IsNullOrWhiteSpace(speaker.Name))
            return "<Unknown1 />";

        // Use the recognized speaker name, sanitized for XML-like tag
        var name = speaker.Name.Replace(" ", "", StringComparison.Ordinal)
            .Replace("<", "", StringComparison.Ordinal)
            .Replace(">", "", StringComparison.Ordinal);

        return $"<{name} />";
    }

    private async Task TrySaveTranscriptRecordAsync(
        string transcript,
        float originalConfidence,
        float[] utteranceAudio,
        SpeakerIdentification? speaker,
        CommandRouteResult? route,
        string? responseText,
        bool commandFiltered,
        CancellationToken ct)
    {
        if (_transcriptStore is null)
        {
            return;
        }

        try
        {
            var stages = new List<PipelineStageTiming>
            {
                new() { Name = "stt", DurationMs = _sttFinalizationMs },
            };

            if (_diarizationEngine?.IsReady == true)
            {
                stages.Add(new PipelineStageTiming { Name = "diarization", DurationMs = _diarizationMs });
            }

            if (_speechEnhancer?.IsReady == true)
            {
                stages.Add(new PipelineStageTiming { Name = "enhancement", DurationMs = _enhancementTotalMs });
            }

            var record = new TranscriptRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = Id,
                Timestamp = DateTimeOffset.UtcNow,
                Text = transcript,
                Confidence = originalConfidence,
                AudioDurationMs = utteranceAudio.Length > 0
                    ? utteranceAudio.Length * 1000.0 / _utteranceSampleRate
                    : 0,
                SampleRate = _utteranceSampleRate,
                SampleCount = utteranceAudio.Length,
                SttModelId = _modelManager?.GetActiveModelId(EngineType.OfflineStt)
                    ?? _modelManager?.GetActiveModelId(EngineType.Stt) ?? "unknown",
                VadModelId = _modelManager?.GetActiveModelId(EngineType.Vad),
                VadActive = _modelManager?.GetActiveModelId(EngineType.Vad) is not null,
                DiarizationModelId = _modelManager?.GetActiveModelId(EngineType.SpeakerEmbedding),
                DiarizationActive = _diarizationEngine?.IsReady == true,
                SpeakerId = speaker?.ProfileId,
                SpeakerName = speaker?.Name,
                SpeakerSimilarity = speaker?.Similarity,
                IsProvisionalSpeaker = speaker is not null ? !speaker.IsAuthorized : null,
                NewProfileCreated = false,
                RouteResult = route is not null ? (route.IsMatch ? "match" : "no_match") : null,
                MatchedSkill = route?.MatchedPattern?.SkillId,
                RouteConfidence = route?.Confidence,
                CommandFiltered = commandFiltered,
                Stages = stages.ToArray(),
                ResponseText = responseText,
            };

            await _transcriptStore.SaveAsync(record, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save transcript record for session {SessionId}", Id);
        }
    }

    private async Task<SpeakerIdentification?> IdentifySpeakerAsync(
        float[] utteranceAudio,
        string? transcript,
        CancellationToken ct)
    {
        if (_diarizationEngine is not { IsReady: true } || _profileStore is null || utteranceAudio.Length == 0)
        {
            return null;
        }

        try
        {
            var sampleRate = _utteranceSampleRate > 0 ? _utteranceSampleRate : 16_000;
            var threshold = _voiceProfileOptions?.CurrentValue.SpeakerVerificationThreshold ?? 0.7f;

            // Use raw (unenhanced) audio for speaker verification when available.
            // Speech enhancement alters spectral characteristics that the embedding
            // model relies on, causing low similarity between enhanced live audio
            // and raw enrollment audio.
            var verificationAudio = _rawUtteranceAudioBuffer.Count > 0
                ? _rawUtteranceAudioBuffer.ToArray()
                : utteranceAudio;

            var embedding = _diarizationEngine.ExtractEmbedding(verificationAudio, sampleRate);
            var profiles = await _profileStore.GetEnrolledProfilesAsync(ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Session {SessionId} speaker verification: {ProfileCount} enrolled profiles, threshold={Threshold:F2}, embedding dim={EmbeddingDim}, audio samples={SampleCount} (raw={IsRaw})",
                Id, profiles.Count, threshold, embedding.Vector.Length, verificationAudio.Length, _rawUtteranceAudioBuffer.Count > 0);

            var speaker = _diarizationEngine.IdentifySpeaker(embedding, profiles, threshold);

            if (speaker is not null)
            {
                if (_adaptiveUpdater is not null)
                {
                    await _adaptiveUpdater.TryUpdateAsync(speaker, embedding, ct).ConfigureAwait(false);
                }

                _eventBus?.Publish(new SpeakerDetectedEvent
                {
                    SessionId = Id,
                    ProfileId = speaker.ProfileId,
                    ProfileName = speaker.Name,
                    Similarity = speaker.Similarity,
                    IsProvisional = false,
                });

                return speaker;
            }

            if (_unknownTracker is not null)
            {
                // Defer unknown speaker tracking and clip saving to background
                // — these involve MongoDB writes that shouldn't block the transcript
                var embeddingCopy = embedding;
                var audioCopy = utteranceAudio;
                var sr = sampleRate;
                var tx = transcript;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var trackingResult = await _unknownTracker.TrackUnknownSpeakerAsync(
                            embeddingCopy, CancellationToken.None).ConfigureAwait(false);

                        if (trackingResult is not null)
                        {
                            _eventBus?.Publish(new SpeakerDetectedEvent
                            {
                                SessionId = Id,
                                ProfileId = trackingResult.Value.Profile.Id,
                                ProfileName = trackingResult.Value.Profile.Name,
                                Similarity = 0f,
                                IsProvisional = true,
                            });

                            if (_audioClipService is not null)
                            {
                                await _audioClipService.SaveClipAsync(
                                    trackingResult.Value.Profile.Id,
                                    audioCopy.AsMemory(), sr, tx,
                                    CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background unknown speaker tracking failed");
                    }
                }, CancellationToken.None);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Speaker verification failed for Wyoming session {SessionId}", Id);
            return null;
        }
    }

    private async Task<CommandRouteResult?> RouteCommandAsync(string transcript, CancellationToken ct)
    {
        if (_commandRouter is null)
        {
            return null;
        }

        try
        {
            return await _commandRouter.RouteAsync(transcript, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command routing failed for Wyoming session {SessionId}", Id);
            return null;
        }
    }

    private async Task<string> DispatchTranscriptAsync(
        string transcript,
        CommandRouteResult? route,
        SpeakerIdentification? speaker,
        CancellationToken ct)
    {
        if (_skillDispatcher is null)
        {
            return transcript;
        }

        try
        {
            if (route is { IsMatch: true })
            {
                var fastPathResult = await _skillDispatcher.DispatchFastPathAsync(route, ct).ConfigureAwait(false);
                return fastPathResult.ResponseText;
            }

            if (_commandRouter is { FallbackToLlmEnabled: false })
            {
                _logger.LogDebug(
                    "No fast-path match and router fallback disabled for Wyoming session {SessionId}",
                    Id);
                return transcript;
            }

            var fallbackResult = await _skillDispatcher.FallbackToLlmAsync(transcript, route, speaker, ct)
                .ConfigureAwait(false);
            return fallbackResult.ResponseText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command dispatch failed for Wyoming session {SessionId}", Id);
            return transcript;
        }
    }

    private void AppendUtteranceAudio(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.Length == 0)
        {
            return;
        }

        if (sampleRate > 0)
        {
            _utteranceSampleRate = sampleRate;
        }

        foreach (var sample in samples)
        {
            _utteranceAudioBuffer.Add(sample);
        }
    }

    private void AppendRawUtteranceAudio(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.Length == 0)
        {
            return;
        }

        if (sampleRate > 0)
        {
            _utteranceSampleRate = sampleRate;
        }

        foreach (var sample in samples)
        {
            _rawUtteranceAudioBuffer.Add(sample);
        }
    }

    private void ResetUtteranceAudio()
    {
        _utteranceAudioBuffer.Clear();
        _rawUtteranceAudioBuffer.Clear();
        _utteranceSampleRate = 16_000;
        _enhancementTotalMs = 0;
        _sttFinalizationMs = 0;
        _diarizationMs = 0;
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
            SetState(WyomingSessionState.Disconnected);
            _client.Dispose();
            _logger.LogDebug(ex, "Failed to send Wyoming error event for session {SessionId}", Id);
        }
    }
}
