using System.Net;
using System.Net.Sockets;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class WyomingProtocolComplianceTests
{
    [Fact]
    public async Task DescribeEvent_ReturnsInfoWithCapabilities()
    {
        var (server, port, services) = await StartTestServerAsync();
        var (client, writer, parser) = await ConnectClientAsync(port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await writer.WriteEventAsync(new DescribeEvent(), cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            var info = Assert.IsType<InfoEvent>(response);
            Assert.NotEmpty(info.Asr ?? []);
            Assert.NotEmpty(info.Wake ?? []);
            Assert.NotNull(info.Tts);
            Assert.False(string.IsNullOrWhiteSpace(info.Version));
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task FullFlow_Detect_Audio_Transcribe()
    {
        var wakeSession = new TestWakeWordSession(
            new WakeWordResult
            {
                Keyword = "hey_lucia",
                Confidence = 0.93f,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(123_456_789L),
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

        var (server, port, services) = await StartTestServerAsync(wakeDetector, sttEngine, vadEngine);
        var (client, writer, parser) = await ConnectClientAsync(port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await writer.WriteEventAsync(new DetectEvent { Names = ["hey_lucia"] }, cts.Token);
            await writer.WriteEventAsync(CreateAudioStartEvent(), cts.Token);
            await writer.WriteEventAsync(CreateAudioChunkEvent(), cts.Token);

            var detection = Assert.IsType<DetectionEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("hey_lucia", detection.Name);
            Assert.Equal(123_456_789L, detection.Timestamp);

            await writer.WriteEventAsync(CreateAudioStartEvent(), cts.Token);
            await writer.WriteEventAsync(CreateAudioChunkEvent(), cts.Token);
            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await writer.WriteEventAsync(new TranscribeEvent { Name = "default", Language = "en" }, cts.Token);

            var transcript = Assert.IsType<TranscriptEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal("turn on the lights", transcript.Text);
            Assert.True(transcript.Confidence > 0);
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }

        Assert.Equal(1, wakeDetector.CreateSessionCount);
        Assert.Equal(1, sttEngine.CreateSessionCount);
        Assert.Equal(1, vadEngine.CreateSessionCount);
        Assert.Equal(1, wakeSession.AcceptAudioChunkCount);
        Assert.Equal(2, sttSession.AcceptAudioChunkCount);
        Assert.Equal(2, vadSession.AcceptAudioChunkCount);
        Assert.Equal(1, vadSession.FlushCallCount);
    }

    [Fact]
    public async Task AudioStopDuringWake_ReturnsNotDetected()
    {
        var (server, port, services) = await StartTestServerAsync(
            wakeWordDetector: new TestWakeWordDetector(new TestWakeWordSession(null)));
        var (client, writer, parser) = await ConnectClientAsync(port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await writer.WriteEventAsync(new DetectEvent { Names = ["hey_lucia"] }, cts.Token);
            await writer.WriteEventAsync(CreateAudioStartEvent(), cts.Token);
            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            Assert.IsType<NotDetectedEvent>(await parser.ReadEventAsync(cts.Token));
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task SynthesizeEvent_ReturnsTtsNotImplemented()
    {
        var (server, port, services) = await StartTestServerAsync();
        var (client, writer, parser) = await ConnectClientAsync(port);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await writer.WriteEventAsync(
                new SynthesizeEvent
                {
                    Text = "hello world",
                    Voice = "default",
                    Language = "en",
                },
                cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            var error = Assert.IsType<ErrorEvent>(response);
            Assert.Equal("tts_not_implemented", error.Code);
            Assert.Equal("Text-to-speech is not yet available", error.Text);
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    private static async Task<(WyomingServer Server, int Port, ServiceProvider Services)> StartTestServerAsync(
        IWakeWordDetector? wakeWordDetector = null,
        ISttEngine? sttEngine = null,
        IVadEngine? vadEngine = null)
    {
        var port = GetRandomPort();
        var options = new WyomingOptions
        {
            Port = port,
            Host = IPAddress.Loopback.ToString(),
            ReadTimeoutSeconds = 5,
        };

        var services = new ServiceCollection();
        services.AddSingleton<IWakeWordDetector>(wakeWordDetector ?? new TestWakeWordDetector(new TestWakeWordSession(null)));
        services.AddSingleton<ISttEngine>(sttEngine ?? new TestSttEngine(new TestSttSession(new SttResult())));
        services.AddSingleton<IVadEngine>(vadEngine ?? new TestVadEngine(
            new TestVadSession(
                new VadSegment
                {
                    Samples = [0.25f, -0.25f],
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromMilliseconds(125),
                    SampleRate = 16_000,
                })));
        services.AddSingleton<WyomingServiceInfo>();
        services.Configure<WyomingOptions>(configuredOptions =>
        {
            configuredOptions.Port = options.Port;
            configuredOptions.Host = options.Host;
            configuredOptions.ReadTimeoutSeconds = options.ReadTimeoutSeconds;
        });
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var server = new WyomingServer(
            Options.Create(options),
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<WyomingServer>>(),
            new SessionEventBus());

        await server.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        return (server, port, serviceProvider);
    }

    private static async Task<(TcpClient Client, WyomingEventWriter Writer, WyomingEventParser Parser)> ConnectClientAsync(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);

        var stream = client.GetStream();
        var options = new WyomingOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            ReadTimeoutSeconds = 5,
        };

        return (client, new WyomingEventWriter(stream), new WyomingEventParser(stream, options));
    }

    private static async Task StopServerAsync(WyomingServer server, ServiceProvider services)
    {
        await server.StopAsync(CancellationToken.None);
        server.Dispose();
        await services.DisposeAsync();
    }

    private static AudioStartEvent CreateAudioStartEvent()
    {
        return new AudioStartEvent
        {
            Rate = 16_000,
            Width = 2,
            Channels = 1,
        };
    }

    private static AudioChunkEvent CreateAudioChunkEvent()
    {
        return new AudioChunkEvent
        {
            Rate = 16_000,
            Width = 2,
            Channels = 1,
            Payload = PcmConverter.Float32ToInt16([0.25f, -0.25f, 0.25f, -0.25f]),
        };
    }

    private static int GetRandomPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
