using System.Net.Sockets;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class WyomingSessionIntegrationTests
{
    [Fact]
    public async Task RunAsync_DescribeEvent_ReturnsInfoEvent()
    {
        var options = CreateOptions();
        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await writer.WriteEventAsync(new DescribeEvent(), cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            var info = Assert.IsType<InfoEvent>(response);
            Assert.NotNull(info.Asr);
            Assert.NotNull(info.Tts);
            Assert.NotNull(info.Wake);
            Assert.NotNull(info.Version);
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

        Assert.Equal(WyomingSessionState.Disconnected, session.State);
    }

    [Fact]
    public async Task RunAsync_DetectAudioAndTranscribe_ReturnsDetectionAndTranscript()
    {
        var options = CreateOptions();
        var detectionTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(123456789L);
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = detectionTimestamp,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var sttSession = new TestSttSession(
            new SttResult
            {
                Text = "turn on the lights",
                Confidence = 0.82f,
            });
        var sttEngine = new TestSttEngine(sttSession);
        var vadSession = new TestVadSession(
            new VadSegment
            {
                Samples = [0.25f, -0.25f, 0.25f, -0.25f],
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromMilliseconds(250),
                SampleRate = 16_000,
            });
        var vadEngine = new TestVadEngine(vadSession);

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            wakeDetector,
            sttEngine,
            vadEngine);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await writer.WriteEventAsync(new DetectEvent { Names = ["hey_lucia"] }, cts.Token);
            await writer.WriteEventAsync(
                new AudioStartEvent
                {
                    Rate = 16_000,
                    Width = 2,
                    Channels = 1,
                },
                cts.Token);
            await writer.WriteEventAsync(
                new AudioChunkEvent
                {
                    Rate = 16_000,
                    Width = 2,
                    Channels = 1,
                    Payload = PcmConverter.Float32ToInt16([0.25f, -0.25f, 0.25f, -0.25f]),
                },
                cts.Token);

            var firstResponse = await parser.ReadEventAsync(cts.Token);
            if (firstResponse is ErrorEvent error)
            {
                throw new Xunit.Sdk.XunitException($"Received error event {error.Code}: {error.Text}");
            }

            var detection = Assert.IsType<DetectionEvent>(firstResponse);
            Assert.Equal("hey_lucia", detection.Name);
            Assert.Equal(detectionTimestamp.ToUnixTimeMilliseconds(), detection.Timestamp);

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await writer.WriteEventAsync(new TranscribeEvent { Name = "default", Language = "en" }, cts.Token);

            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("turn on the lights", transcript.Text);
            Assert.Equal(1.0f, transcript.Confidence);
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

        Assert.Equal(1, wakeDetector.CreateSessionCount);
        Assert.Equal(1, sttEngine.CreateSessionCount);
        Assert.Equal(1, vadEngine.CreateSessionCount);
        Assert.Equal(1, wakeSession.AcceptAudioChunkCount);
        Assert.Equal(1, sttSession.AcceptAudioChunkCount);
        Assert.Equal(1, vadSession.AcceptAudioChunkCount);
        Assert.Equal(1, vadSession.FlushCallCount);
    }

    [Fact]
    public async Task RunAsync_TranscribeWithKnownSpeaker_UpdatesProfileAndRoutesTranscript()
    {
        var options = CreateOptions();
        var detectionTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(123456789L);
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = detectionTimestamp,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var sttSession = new TestSttSession(
            new SttResult
            {
                Text = "turn on the office lights",
                Confidence = 0.88f,
            });
        var sttEngine = new TestSttEngine(sttSession);
        var vadSegment = new VadSegment
        {
            Samples = [0.1f, 0.2f, 0.3f, 0.4f],
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromMilliseconds(250),
            SampleRate = 16_000,
        };
        var vadSession = new TestVadSession(vadSegment);
        var vadEngine = new TestVadEngine(vadSession);
        var embedding = Enumerable.Range(0, 128)
            .Select(static i => (float)i / 128)
            .ToArray();
        var speaker = new SpeakerIdentification
        {
            ProfileId = "alice",
            Name = "Alice",
            Similarity = 0.95f,
            IsAuthorized = true,
        };
        var diarizationEngine = new TestDiarizationEngine(speaker, embedding);
        var profileStore = new InMemorySpeakerProfileStore();
        var profile = new SpeakerProfile
        {
            Id = "alice",
            Name = "Alice",
            AverageEmbedding = embedding,
            Embeddings = [embedding],
        };
        await profileStore.CreateAsync(profile, CancellationToken.None);

        var router = new TestCommandRouter(CommandRouteResult.NoMatch(TimeSpan.Zero));
        var voiceOptions = Options.Create(new VoiceProfileOptions());
        var adaptiveUpdater = new AdaptiveProfileUpdater(
            profileStore,
            voiceOptions,
            NullLogger<AdaptiveProfileUpdater>.Instance);

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            wakeDetector,
            sttEngine,
            vadEngine,
            configureServices: serviceCollection =>
            {
                serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                serviceCollection.AddSingleton<ISpeakerProfileStore>(profileStore);
                serviceCollection.AddSingleton(adaptiveUpdater);
                serviceCollection.AddSingleton<ICommandRouter>(router);
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await WriteWakeAndSpeechAsync(writer, cts.Token);

            _ = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await writer.WriteEventAsync(new TranscribeEvent { Name = "default", Language = "en" }, cts.Token);

            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("turn on the office lights", transcript.Text);
            Assert.Equal(1.0f, transcript.Confidence);
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

        Assert.Equal(1, diarizationEngine.ExtractEmbeddingCallCount);
        Assert.Equal(1, diarizationEngine.IdentifySpeakerCallCount);
        Assert.Equal(16_000, diarizationEngine.LastSampleRate);
        Assert.Equal(vadSegment.Samples, diarizationEngine.LastAudioSamples);
        Assert.Equal(1, router.RouteCallCount);
        Assert.Equal("turn on the office lights", router.LastTranscript);

        var updatedProfile = await profileStore.GetAsync("alice", CancellationToken.None);
        Assert.NotNull(updatedProfile);
        Assert.Equal(1, updatedProfile.InteractionCount);
    }

    [Fact]
    public async Task RunAsync_UnknownSpeakerWithFilter_TracksSpeakerAndSuppressesTranscript()
    {
        var options = new WyomingOptions
        {
            ReadTimeoutSeconds = 1,
        };
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = DateTimeOffset.UtcNow,
            });
        var wakeDetector = new TestWakeWordDetector(wakeSession);
        var sttSession = new TestSttSession(
            new SttResult
            {
                Text = "unlock the front door",
                Confidence = 0.91f,
            });
        var sttEngine = new TestSttEngine(sttSession);
        var vadEngine = new TestVadEngine(
            new TestVadSession(
                new VadSegment
                {
                    Samples = [0.15f, 0.05f, -0.05f, -0.15f],
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromMilliseconds(250),
                    SampleRate = 16_000,
                }));
        var diarizationEngine = new TestDiarizationEngine();
        var profileStore = new InMemorySpeakerProfileStore();
        var voiceOptions = Options.Create(
            new VoiceProfileOptions
            {
                IgnoreUnknownVoices = true,
            });
        var router = new TestCommandRouter(CommandRouteResult.NoMatch(TimeSpan.Zero));
        var unknownTracker = new UnknownSpeakerTracker(
            profileStore,
            voiceOptions,
            NullLogger<UnknownSpeakerTracker>.Instance);
        var speakerFilter = new SpeakerVerificationFilter(
            voiceOptions,
            NullLogger<SpeakerVerificationFilter>.Instance);

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            wakeDetector,
            sttEngine,
            vadEngine,
            configureServices: serviceCollection =>
            {
                serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                serviceCollection.AddSingleton<ISpeakerProfileStore>(profileStore);
                serviceCollection.AddSingleton(unknownTracker);
                serviceCollection.AddSingleton(speakerFilter);
                serviceCollection.AddSingleton<ICommandRouter>(router);
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await WriteWakeAndSpeechAsync(writer, cts.Token);

            _ = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await writer.WriteEventAsync(new TranscribeEvent { Name = "default", Language = "en" }, cts.Token);

            await Assert.ThrowsAsync<WyomingProtocolException>(() => parser.ReadEventAsync(CancellationToken.None));
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

        Assert.Equal(1, diarizationEngine.ExtractEmbeddingCallCount);
        Assert.Equal(1, router.RouteCallCount);

        var provisionalProfiles = await profileStore.GetProvisionalProfilesAsync(CancellationToken.None);
        Assert.Single(provisionalProfiles);
    }

    private static WyomingOptions CreateOptions()
    {
        return new WyomingOptions
        {
            ReadTimeoutSeconds = 5,
        };
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
            services.AddSingleton<IWakeWordDetector>(wakeWordDetector);
        }

        if (sttEngine is not null)
        {
            services.AddSingleton<ISttEngine>(sttEngine);
        }

        if (vadEngine is not null)
        {
            services.AddSingleton<IVadEngine>(vadEngine);
        }

        configureServices?.Invoke(services);

        var serviceProvider = services.BuildServiceProvider();
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();

        var endpoint = (System.Net.IPEndPoint)listener.LocalEndpoint;
        var acceptTask = listener.AcceptTcpClientAsync();
        var client = new TcpClient();
        await client.ConnectAsync(endpoint.Address, endpoint.Port);
        var serverClient = await acceptTask;

        var session = new WyomingSession(serverClient, serviceProvider, NullLogger<WyomingSession>.Instance, options);
        var stream = client.GetStream();

        return (
            listener,
            client,
            serverClient,
            serviceProvider,
            session,
            new WyomingEventWriter(stream),
            new WyomingEventParser(stream, options));
    }

    private static async Task WriteWakeAndSpeechAsync(WyomingEventWriter writer, CancellationToken ct)
    {
        await writer.WriteEventAsync(new DetectEvent { Names = ["hey_lucia"] }, ct);
        await writer.WriteEventAsync(
            new AudioStartEvent
            {
                Rate = 16_000,
                Width = 2,
                Channels = 1,
            },
            ct);
        await writer.WriteEventAsync(
            new AudioChunkEvent
            {
                Rate = 16_000,
                Width = 2,
                Channels = 1,
                Payload = PcmConverter.Float32ToInt16([0.25f, -0.25f, 0.25f, -0.25f]),
            },
            ct);
    }
}
