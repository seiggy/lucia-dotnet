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

public sealed class PostSttPipelineTests
{
    [Fact]
    public async Task RunAsync_KnownSpeakerAndMatchedRoute_UsesFastPathDispatch()
    {
        var options = CreateOptions();
        var diarizationEngine = new TestDiarizationEngine(
            new SpeakerIdentification
            {
                ProfileId = "alice",
                Name = "Alice",
                Similarity = 0.96f,
                IsAuthorized = true,
            },
            CreateEmbeddingVector(0.2f, 0.001f));
        var profileStore = new InMemorySpeakerProfileStore();
        await profileStore.CreateAsync(
            new SpeakerProfile
            {
                Id = "alice",
                Name = "Alice",
                AverageEmbedding = CreateEmbeddingVector(0.2f, 0.001f),
                Embeddings = [CreateEmbeddingVector(0.2f, 0.001f)],
            },
            CancellationToken.None);

        var router = new TestCommandRouter(CreateMatchedRoute());
        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            new TestWakeWordDetector(CreateWakeSession()),
            new TestSttEngine(CreateSttSession("turn on the office lights")),
            new TestVadEngine(CreateVadSession()),
            serviceCollection =>
            {
                serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                serviceCollection.AddSingleton<ISpeakerProfileStore>(profileStore);
                serviceCollection.AddSingleton<ICommandRouter>(router);
                serviceCollection.AddSingleton(sp => new SkillDispatcher(sp, NullLogger<SkillDispatcher>.Instance));
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
            Assert.Equal("<Alice />turn on the office lights", transcript.Text);
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
    }

    [Fact]
    public async Task RunAsync_UnknownSpeakerWithIgnoreEnabled_SilentlyDropsCommand()
    {
        var options = CreateOptions(readTimeoutSeconds: 1);
        var diarizationEngine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.4f, 0.001f));
        var profileStore = new InMemorySpeakerProfileStore();
        var voiceOptions = Options.Create(new VoiceProfileOptions { IgnoreUnknownVoices = true });
        var router = new TestCommandRouter(CreateMatchedRoute());
        var unknownTracker = new UnknownSpeakerTracker(profileStore, voiceOptions, NullLogger<UnknownSpeakerTracker>.Instance);
        var speakerFilter = new SpeakerVerificationFilter(voiceOptions, NullLogger<SpeakerVerificationFilter>.Instance);

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            new TestWakeWordDetector(CreateWakeSession()),
            new TestSttEngine(CreateSttSession("unlock the front door")),
            new TestVadEngine(CreateVadSession()),
            serviceCollection =>
            {
                serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                serviceCollection.AddSingleton<ISpeakerProfileStore>(profileStore);
                serviceCollection.AddSingleton(unknownTracker);
                serviceCollection.AddSingleton(speakerFilter);
                serviceCollection.AddSingleton<ICommandRouter>(router);
                serviceCollection.AddSingleton(sp => new SkillDispatcher(sp, NullLogger<SkillDispatcher>.Instance));
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runTask = session.RunAsync(cts.Token);

        try
        {
            await WriteWakeAndSpeechAsync(writer, cts.Token);
            _ = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));

            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            // AudioStop now triggers the full STT pipeline; read the transcript response
            var response = await parser.ReadEventAsync(cts.Token);
            var transcriptEvent = Assert.IsType<TranscriptEvent>(response);

            // Unknown speaker: transcript is returned with Unknown speaker tag
            Assert.Equal("<Unknown1 />unlock the front door", transcriptEvent.Text);
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

        Assert.Single(await profileStore.GetProvisionalProfilesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_RouterFailure_ReturnsOriginalTranscript()
    {
        var options = CreateOptions();
        var router = new TestCommandRouter(CommandRouteResult.NoMatch(TimeSpan.Zero))
        {
            ShouldThrow = true,
            FallbackToLlmEnabled = false,
        };

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            new TestWakeWordDetector(CreateWakeSession()),
            new TestSttEngine(CreateSttSession("set a timer for ten minutes")),
            new TestVadEngine(CreateVadSession()),
            serviceCollection =>
            {
                serviceCollection.AddSingleton<ICommandRouter>(router);
                serviceCollection.AddSingleton(sp => new SkillDispatcher(sp, NullLogger<SkillDispatcher>.Instance));
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
            Assert.Equal("<Unknown1 />set a timer for ten minutes", transcript.Text);
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
    }

    [Fact]
    public async Task RunAsync_DiarizationFailure_ContinuesWithoutSpeakerId()
    {
        var options = CreateOptions();
        var diarizationEngine = new TestDiarizationEngine(embeddingVector: CreateEmbeddingVector(0.5f, 0.001f))
        {
            ShouldThrow = true,
        };
        var router = new TestCommandRouter(CreateMatchedRoute());
        var profileStore = new InMemorySpeakerProfileStore();

        var (listener, client, serverClient, services, session, writer, parser) = await CreateConnectedSessionAsync(
            options,
            new TestWakeWordDetector(CreateWakeSession()),
            new TestSttEngine(CreateSttSession("turn off the bedroom lights")),
            new TestVadEngine(CreateVadSession()),
            serviceCollection =>
            {
                serviceCollection.AddSingleton<IDiarizationEngine>(diarizationEngine);
                serviceCollection.AddSingleton<ISpeakerProfileStore>(profileStore);
                serviceCollection.AddSingleton<ICommandRouter>(router);
                serviceCollection.AddSingleton(sp => new SkillDispatcher(sp, NullLogger<SkillDispatcher>.Instance));
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
            // Diarization failure: falls back to Unknown speaker tag
            Assert.Equal("<Unknown1 />turn off the bedroom lights", transcript.Text);
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
    }

    private static WyomingOptions CreateOptions(int readTimeoutSeconds = 5)
    {
        return new WyomingOptions
        {
            ReadTimeoutSeconds = readTimeoutSeconds,
        };
    }

    private static CommandRouteResult CreateMatchedRoute()
    {
        return new CommandRouteResult
        {
            IsMatch = true,
            Confidence = 0.97f,
            MatchDuration = TimeSpan.FromMilliseconds(2),
            CapturedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = "on",
                ["entity"] = "office lights",
            },
            MatchedPattern = new CommandPattern
            {
                Id = "lights-toggle",
                SkillId = "LightControlSkill",
                Action = "toggle",
                Templates = ["turn {action:on|off} [the] {entity}"],
            },
        };
    }

    private static TestWakeWordSession CreateWakeSession()
    {
        return new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.92f,
                Timestamp = DateTimeOffset.UtcNow,
            });
    }

    private static TestSttSession CreateSttSession(string transcript)
    {
        return new TestSttSession(
            new SttResult
            {
                Text = transcript,
                Confidence = 0.9f,
            });
    }

    private static TestVadSession CreateVadSession()
    {
        return new TestVadSession(
            new VadSegment
            {
                Samples = [0.25f, -0.25f, 0.25f, -0.25f],
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromMilliseconds(250),
                SampleRate = 16_000,
            });
    }

    private static float[] CreateEmbeddingVector(float startValue, float increment)
    {
        return Enumerable.Range(0, 128)
            .Select(i => startValue + (i * increment))
            .ToArray();
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
        IWakeWordDetector wakeWordDetector,
        ISttEngine sttEngine,
        IVadEngine vadEngine,
        Action<ServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<WyomingOptions>>(Options.Create(options));
        services.AddSingleton(wakeWordDetector);
        services.AddSingleton(sttEngine);
        services.AddSingleton(vadEngine);
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
