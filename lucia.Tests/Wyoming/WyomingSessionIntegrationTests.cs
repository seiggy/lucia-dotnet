using System.Net.Sockets;
using lucia.Wyoming.Audio;
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
            Assert.Equal(0.82f, transcript.Confidence);
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
        IVadEngine? vadEngine = null)
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
}
