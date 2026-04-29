using System.Net;
using System.Net.Sockets;
using lucia.Wyoming.Audio;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

/// <summary>
/// Tests for the feature-flagged GTCRN enhanced-clip pipeline in WyomingSession.
/// When <see cref="SpeechEnhancementOptions.UseEnhancedClipForStt"/> is enabled,
/// the enhanced utterance clip is re-transcribed through a fresh STT session and
/// used for speaker verification instead of raw audio.
/// </summary>
public sealed class EnhancedClipPipelineTests
{
    // --- Flag OFF (default) — existing behavior preserved ---

    [Fact]
    public async Task FlagOff_SttUsesRawTranscript_NotReTranscribed()
    {
        // With enhancer active but flag OFF, the transcript should come from
        // the streaming STT session (raw audio), not a re-transcription pass.
        var rawSession = new CapturingSttSession(new SttResult { Text = "raw transcript", Confidence = 0.85f });
        var reTranscribeSession = new CapturingSttSession(new SttResult { Text = "enhanced transcript", Confidence = 0.95f });
        var sttEngine = new QueuedSttEngine();
        sttEngine.Enqueue(rawSession);
        sttEngine.Enqueue(reTranscribeSession);

        var enhancerSession = new ConfigurableEnhancerSession(samples => AmplifyAudio(samples, 2.0f));
        var enhancer = new ConfigurableEnhancer(enhancerSession);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = false };

        var transcript = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options);

        Assert.Contains("raw transcript", transcript.Text);
        Assert.DoesNotContain("enhanced transcript", transcript.Text);
        Assert.Equal(1, sttEngine.CreateSessionCount);
    }

    [Fact]
    public async Task FlagOff_SpeakerVerificationUsesRawAudio()
    {
        // With enhancer active but flag OFF, speaker verification should use
        // raw audio from _rawUtteranceAudioBuffer (not enhanced audio).
        var rawSession = new TestSttSession(new SttResult { Text = "hello", Confidence = 0.8f });
        var sttEngine = new TestSttEngine(rawSession);

        var enhancerSession = new ConfigurableEnhancerSession(samples => AmplifyAudio(samples, 2.0f));
        var enhancer = new ConfigurableEnhancer(enhancerSession);

        var speaker = new SpeakerIdentification
        {
            ProfileId = "alice", Name = "Alice", Similarity = 0.95f, IsAuthorized = true,
        };
        var diarizationEngine = new TestDiarizationEngine(speaker);
        var profileStore = new InMemorySpeakerProfileStore();
        await profileStore.CreateAsync(CreateAliceProfile(diarizationEngine), CancellationToken.None);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = false };

        _ = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options,
            diarizationEngine: diarizationEngine,
            profileStore: profileStore);

        // Raw audio: the PCM round-trip of [0.25, -0.25, 0.25, -0.25]
        // should NOT be amplified (enhanced) for speaker verification.
        Assert.Equal(1, diarizationEngine.ExtractEmbeddingCallCount);
        Assert.True(diarizationEngine.LastAudioSamples.Length > 0, "Should have audio samples for verification");

        // Raw audio should have values in the original range (~0.25), not amplified (~0.5)
        var maxSample = diarizationEngine.LastAudioSamples.Max(MathF.Abs);
        Assert.True(maxSample < 0.4f,
            $"Speaker verification used enhanced audio (max={maxSample:F3}), expected raw (~0.25)");
    }

    [Fact]
    public async Task FlagOff_EnhancedAudioStillStoredInUtteranceBuffer()
    {
        // Even with flag OFF, the enhancer should process audio and the enhanced
        // version should be stored (for clip storage). Verify the enhancer was called.
        var rawSession = new TestSttSession(new SttResult { Text = "hello", Confidence = 0.8f });
        var sttEngine = new TestSttEngine(rawSession);

        var enhancerSession = new ConfigurableEnhancerSession(samples => AmplifyAudio(samples, 2.0f));
        var enhancer = new ConfigurableEnhancer(enhancerSession);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = false };

        _ = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options);

        Assert.True(enhancerSession.ProcessCount > 0,
            "Enhancer session should have been called even with flag OFF (for clip storage)");
    }

    // --- Flag ON — enhanced clip path ---

    [Fact]
    public async Task FlagOn_SttReTranscribesWithEnhancedClip()
    {
        // With flag ON, the enhanced utterance clip should be fed to a fresh
        // STT session for re-transcription, replacing the raw result.
        var rawSession = new CapturingSttSession(new SttResult { Text = "raw noisy result", Confidence = 0.70f });
        var reTranscribeSession = new CapturingSttSession(new SttResult { Text = "enhanced clean result", Confidence = 0.95f });
        var sttEngine = new QueuedSttEngine();
        sttEngine.Enqueue(rawSession);
        sttEngine.Enqueue(reTranscribeSession);

        var enhancerSession = new ConfigurableEnhancerSession(samples => AmplifyAudio(samples, 2.0f));
        var enhancer = new ConfigurableEnhancer(enhancerSession);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = true };

        var transcript = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options);

        Assert.Contains("enhanced clean result", transcript.Text);
        Assert.DoesNotContain("raw noisy result", transcript.Text);
        Assert.Equal(2, sttEngine.CreateSessionCount);
    }

    [Fact]
    public async Task FlagOn_SpeakerVerificationUsesEnhancedAudio()
    {
        // With flag ON, speaker verification should receive enhanced audio
        // (from _utteranceAudioBuffer) instead of raw audio.
        var rawSession = new CapturingSttSession(new SttResult { Text = "hello", Confidence = 0.8f });
        var reTranscribeSession = new CapturingSttSession(new SttResult { Text = "hello enhanced", Confidence = 0.9f });
        var sttEngine = new QueuedSttEngine();
        sttEngine.Enqueue(rawSession);
        sttEngine.Enqueue(reTranscribeSession);

        var enhancerSession = new ConfigurableEnhancerSession(samples => AmplifyAudio(samples, 2.0f));
        var enhancer = new ConfigurableEnhancer(enhancerSession);

        var speaker = new SpeakerIdentification
        {
            ProfileId = "alice", Name = "Alice", Similarity = 0.95f, IsAuthorized = true,
        };
        var diarizationEngine = new TestDiarizationEngine(speaker);
        var profileStore = new InMemorySpeakerProfileStore();
        await profileStore.CreateAsync(CreateAliceProfile(diarizationEngine), CancellationToken.None);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = true };

        _ = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options,
            diarizationEngine: diarizationEngine,
            profileStore: profileStore);

        Assert.Equal(1, diarizationEngine.ExtractEmbeddingCallCount);
        Assert.True(diarizationEngine.LastAudioSamples.Length > 0, "Should have audio for verification");

        // Enhanced audio should have amplified values (~0.5), not raw (~0.25)
        var maxSample = diarizationEngine.LastAudioSamples.Max(MathF.Abs);
        Assert.True(maxSample > 0.4f,
            $"Speaker verification used raw audio (max={maxSample:F3}), expected enhanced (~0.5)");
    }

    [Fact]
    public async Task FlagOn_ReTranscriptionFeedsNonEmptyEnhancedClip()
    {
        // Verify the re-transcription session receives a non-empty enhanced clip
        // with the correct number of samples.
        var rawSession = new CapturingSttSession(new SttResult { Text = "raw", Confidence = 0.7f });
        var reTranscribeSession = new CapturingSttSession(new SttResult { Text = "enhanced", Confidence = 0.9f });
        var sttEngine = new QueuedSttEngine();
        sttEngine.Enqueue(rawSession);
        sttEngine.Enqueue(reTranscribeSession);

        var enhancerSession = new ConfigurableEnhancerSession(samples => AmplifyAudio(samples, 2.0f));
        var enhancer = new ConfigurableEnhancer(enhancerSession);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = true };

        _ = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options);

        Assert.True(reTranscribeSession.CapturedSamples.Count > 0,
            "Re-transcription session should receive enhanced audio samples");
        Assert.Equal(rawSession.CapturedSamples.Count, reTranscribeSession.CapturedSamples.Count);
    }

    // --- Edge cases ---

    [Fact]
    public async Task FlagOn_NoEnhancerAvailable_FallsBackToRawTranscript()
    {
        // When flag is ON but no ISpeechEnhancer is registered, the session
        // should gracefully fall back to the raw STT result.
        var rawSession = new TestSttSession(new SttResult { Text = "raw fallback", Confidence = 0.8f });
        var sttEngine = new TestSttEngine(rawSession);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = true };

        var transcript = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: null,
            enhancementOptions: options);

        Assert.Contains("raw fallback", transcript.Text);
        Assert.Equal(1, sttEngine.CreateSessionCount);
    }

    [Fact]
    public async Task FlagOn_EnhancerReturnsEmptyBuffer_FallsBackToRawTranscript()
    {
        // When the enhancer returns an empty buffer, enhanced utterance audio is empty,
        // so the re-transcription guard (utteranceAudio.Length > 0) prevents re-transcription.
        var rawSession = new TestSttSession(new SttResult { Text = "raw from empty enhance", Confidence = 0.82f });
        var sttEngine = new TestSttEngine(rawSession);

        var enhancerSession = new ConfigurableEnhancerSession(_ => []);
        var enhancer = new ConfigurableEnhancer(enhancerSession);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = true };

        var transcript = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options);

        Assert.Contains("raw from empty enhance", transcript.Text);
        Assert.Equal(1, sttEngine.CreateSessionCount);
    }

    [Fact]
    public async Task FlagOn_EnhancerNotReady_FallsBackToRawTranscript()
    {
        // When the enhancer is registered but IsReady=false, no session is created
        // and the pipeline falls back to raw audio for everything.
        var rawSession = new TestSttSession(new SttResult { Text = "raw not ready", Confidence = 0.78f });
        var sttEngine = new TestSttEngine(rawSession);

        var enhancer = new ConfigurableEnhancer(session: null, isReady: false);

        var options = new SpeechEnhancementOptions { UseEnhancedClipForStt = true };

        var transcript = await RunPipelineAndGetTranscriptAsync(
            sttEngine,
            enhancer: enhancer,
            enhancementOptions: options);

        Assert.Contains("raw not ready", transcript.Text);
        Assert.Equal(1, sttEngine.CreateSessionCount);
    }

    // --- Infrastructure ---

    /// <summary>
    /// Amplifies audio samples by a given factor — produces distinguishable enhanced audio.
    /// </summary>
    private static float[] AmplifyAudio(float[] samples, float factor) =>
        samples.Select(s => Math.Clamp(s * factor, -1.0f, 1.0f)).ToArray();

    private static SpeakerProfile CreateAliceProfile(TestDiarizationEngine engine)
    {
        var embedding = Enumerable.Range(0, 128).Select(static i => (float)i / 128).ToArray();
        return new SpeakerProfile
        {
            Id = "alice",
            Name = "Alice",
            AverageEmbedding = embedding,
            Embeddings = [embedding],
        };
    }

    private static async Task<TranscriptEvent> RunPipelineAndGetTranscriptAsync(
        ISttEngine sttEngine,
        ISpeechEnhancer? enhancer,
        SpeechEnhancementOptions? enhancementOptions = null,
        TestDiarizationEngine? diarizationEngine = null,
        ISpeakerProfileStore? profileStore = null)
    {
        enhancementOptions ??= new SpeechEnhancementOptions();

        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = DateTimeOffset.UtcNow,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var vadSession = new TestVadSession(
            new VadSegment
            {
                Samples = [0.25f, -0.25f, 0.25f, -0.25f],
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromMilliseconds(250),
                SampleRate = 16_000,
            });
        var vadEngine = new TestVadEngine(vadSession);

        var wyomingOptions = new WyomingOptions { ReadTimeoutSeconds = 5 };

        var (listener, client, serverClient, services, session, writer, parser) =
            await CreateConnectedSessionAsync(
                wyomingOptions, wakeDetector, sttEngine, vadEngine,
                serviceCollection =>
                {
                    serviceCollection.AddSingleton(
                        Options.Create(enhancementOptions));
                    serviceCollection.AddSingleton<IOptionsMonitor<SpeechEnhancementOptions>>(
                        new OptionsMonitorStub<SpeechEnhancementOptions>(enhancementOptions));

                    if (enhancer is not null)
                    {
                        serviceCollection.AddSingleton(enhancer);
                    }

                    if (diarizationEngine is not null)
                    {
                        serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                    }

                    if (profileStore is not null)
                    {
                        serviceCollection.AddSingleton(profileStore);
                    }
                });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        TranscriptEvent transcript;
        try
        {
            // Wake word → audio start → audio chunk → detection
            await writer.WriteEventAsync(new DetectEvent { Names = ["hey_lucia"] }, cts.Token);
            await writer.WriteEventAsync(
                new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 }, cts.Token);
            await writer.WriteEventAsync(
                new AudioChunkEvent
                {
                    Rate = 16_000, Width = 2, Channels = 1,
                    Payload = PcmConverter.Float32ToInt16([0.25f, -0.25f, 0.25f, -0.25f]),
                }, cts.Token);

            var detection = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("hey_lucia", detection.Name);

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
        }
        finally
        {
            client.Close();
            await runTask;
            serverClient.Dispose();
            client.Dispose();
            listener.Stop();
            await services.DisposeAsync();
        }

        return transcript;
    }

    private static async Task<(
        TcpListener Listener,
        TcpClient Client,
        TcpClient ServerClient,
        ServiceProvider Services,
        WyomingSession Session,
        WyomingEventWriter Writer,
        WyomingEventParser Parser)> CreateConnectedSessionAsync(
        WyomingOptions options,
        IWakeWordDetector? wakeWordDetector = null,
        ISttEngine? sttEngine = null,
        IVadEngine? vadEngine = null,
        Action<ServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WyomingOptions>>(Options.Create(options));

        if (wakeWordDetector is not null)
        {
            services.AddSingleton(wakeWordDetector);
        }

        if (sttEngine is not null)
        {
            services.AddSingleton(sttEngine);
        }

        if (vadEngine is not null)
        {
            services.AddSingleton(vadEngine);
        }

        configureServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var acceptTask = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        var serverClient = await acceptTask;

        var session = new WyomingSession(
            serverClient, serviceProvider, NullLogger<WyomingSession>.Instance, options);
        var stream = client.GetStream();

        return (
            listener, client, serverClient, serviceProvider,
            session, new WyomingEventWriter(stream), new WyomingEventParser(stream, options));
    }

    // --- Test doubles ---

    /// <summary>
    /// STT session that captures all audio samples fed to it for later assertion.
    /// </summary>
    private sealed class CapturingSttSession(SttResult finalResult) : ISttSession
    {
        private readonly List<float> _capturedSamples = [];

        public IReadOnlyList<float> CapturedSamples => _capturedSamples;
        public int AcceptAudioChunkCount { get; private set; }
        public bool IsEndOfUtterance => false;

        public void AcceptAudioChunk(ReadOnlySpan<float> samples, int sampleRate)
        {
            AcceptAudioChunkCount++;
            _capturedSamples.AddRange(samples.ToArray());
        }

        public SttResult GetPartialResult() => new();
        public Task<SttResult> GetFinalResultAsync() => Task.FromResult(finalResult);
        public void Dispose() { }
    }

    /// <summary>
    /// STT engine that returns pre-queued sessions in order, enabling different
    /// sessions for the streaming pass and the re-transcription pass.
    /// </summary>
    private sealed class QueuedSttEngine : ISttEngine
    {
        private readonly Queue<ISttSession> _sessions = new();

        public bool IsReady => true;
        public int CreateSessionCount { get; private set; }

        public void Enqueue(ISttSession session) => _sessions.Enqueue(session);

        public ISttSession CreateSession()
        {
            CreateSessionCount++;
            return _sessions.Dequeue();
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Configurable speech enhancer session for testing. Transform function
    /// controls what the "enhanced" audio looks like.
    /// </summary>
    private sealed class ConfigurableEnhancerSession(Func<float[], float[]> processFunc) : ISpeechEnhancerSession
    {
        public int ProcessCount { get; private set; }

        public float[] Process(float[] samples)
        {
            ProcessCount++;
            return processFunc(samples);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Configurable speech enhancer with controllable readiness state.
    /// </summary>
    private sealed class ConfigurableEnhancer : ISpeechEnhancer
    {
        private readonly ISpeechEnhancerSession? _session;

        public ConfigurableEnhancer(ISpeechEnhancerSession? session = null, bool isReady = true)
        {
            _session = session;
            IsReady = isReady && session is not null;
        }

        public bool IsReady { get; }

        public ISpeechEnhancerSession CreateSession() =>
            _session ?? throw new InvalidOperationException("Enhancer not configured with a session");
    }

    private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
