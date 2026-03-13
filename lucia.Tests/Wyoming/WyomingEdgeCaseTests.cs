using System.Net.Sockets;
using System.Text;
using lucia.Wyoming.Audio;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class WyomingEdgeCaseTests
{
    [Fact]
    public async Task StartAsync_RapidConnectDisconnect_DoesNotLeakSessions()
    {
        var (server, services, options) = await StartServerAsync();

        try
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, options.Port);

                await WaitUntilAsync(
                    () => server.ActiveSessionCount == 1,
                    $"Expected one active session after connect attempt {attempt + 1}.");

                client.Close();

                await WaitUntilAsync(
                    () => server.ActiveSessionCount == 0,
                    $"Expected all sessions cleaned up after disconnect attempt {attempt + 1}.");
            }

            Assert.Equal(0, server.ActiveSessionCount);
        }
        finally
        {
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task RunAsync_EmptyAudioStream_DoesNotCrash()
    {
        var (server, services, options) = await StartServerAsync();
        var (client, writer, parser) = await ConnectClientAsync(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await writer.WriteEventAsync(
                new AudioStartEvent
                {
                    Rate = 16_000,
                    Width = 2,
                    Channels = 1,
                },
                cts.Token);
            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);
            await writer.WriteEventAsync(new DescribeEvent(), cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            var info = Assert.IsType<InfoEvent>(response);
            Assert.NotNull(info.Version);
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task RunAsync_OversizedPayload_RejectedWithProtocolError()
    {
        var options = CreateOptions();
        options.MaxPayloadLength = 8;

        var (server, services, serverOptions) = await StartServerAsync(options);
        var (client, _, parser) = await ConnectClientAsync(serverOptions);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await WriteRawHeaderAsync(
                client,
                """{"type":"audio-chunk","data":{"rate":16000,"width":2,"channels":1},"payload_length":9}""",
                cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            var error = Assert.IsType<ErrorEvent>(response);
            Assert.Equal("protocol_error", error.Code);
            Assert.Contains("payload_length 9 exceeds maximum 8", error.Text);

            await WaitUntilAsync(
                () => server.ActiveSessionCount == 0,
                "Expected oversized payload rejection to end the session.");
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task RunAsync_InvalidJsonHeader_ReturnsProtocolError()
    {
        var (server, services, options) = await StartServerAsync();
        var (client, _, parser) = await ConnectClientAsync(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await WriteRawHeaderAsync(client, "this is not json", cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            var error = Assert.IsType<ErrorEvent>(response);
            Assert.Equal("protocol_error", error.Code);
            Assert.Contains("Failed to parse event header.", error.Text);

            await WaitUntilAsync(
                () => server.ActiveSessionCount == 0,
                "Expected invalid header rejection to end the session.");
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task RunAsync_DoubleAudioStartWithoutStop_IsHandledGracefully()
    {
        var wakeWordDetector = new TestWakeWordDetector(new TestWakeWordSession(null));
        var (server, services, options) = await StartServerAsync(wakeWordDetector: wakeWordDetector);
        var (client, writer, parser) = await ConnectClientAsync(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

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
                new AudioStartEvent
                {
                    Rate = 22_050,
                    Width = 2,
                    Channels = 1,
                },
                cts.Token);
            await writer.WriteEventAsync(new AudioStopEvent(), cts.Token);

            Assert.IsType<NotDetectedEvent>(await parser.ReadEventAsync(cts.Token));

            await writer.WriteEventAsync(new DescribeEvent(), cts.Token);
            Assert.IsType<InfoEvent>(await parser.ReadEventAsync(cts.Token));
            Assert.Equal(1, wakeWordDetector.CreateSessionCount);
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task RunAsync_TranscribeWithoutAudio_RespondsGracefully()
    {
        var (server, services, options) = await StartServerAsync();
        var (client, writer, parser) = await ConnectClientAsync(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await writer.WriteEventAsync(new TranscribeEvent { Name = "default", Language = "en" }, cts.Token);

            var response = await parser.ReadEventAsync(cts.Token);

            switch (response)
            {
                case ErrorEvent error:
                    Assert.False(string.IsNullOrWhiteSpace(error.Code));
                    Assert.False(string.IsNullOrWhiteSpace(error.Text));
                    break;

                case TranscriptEvent transcript:
                    Assert.Equal(string.Empty, transcript.Text);
                    Assert.Equal(0f, transcript.Confidence);
                    break;

                default:
                    throw new Xunit.Sdk.XunitException(
                        $"Expected an error or transcript response, but received {response?.GetType().Name ?? "null"}.");
            }

            await writer.WriteEventAsync(new DescribeEvent(), cts.Token);
            Assert.IsType<InfoEvent>(await parser.ReadEventAsync(cts.Token));
        }
        finally
        {
            client.Close();
            client.Dispose();
            await StopServerAsync(server, services);
        }
    }

    [Fact]
    public async Task StartAsync_ConcurrentAudioFromTwoClients_DoesNotInterfere()
    {
        var wakeWordDetector = new TestWakeWordDetector(new TestWakeWordSession(null));
        var sttEngine = new TestSttEngine(new TestSttSession(new SttResult { Text = "unused" }));
        var vadEngine = new TestVadEngine(
            new TestVadSession(
                new VadSegment
                {
                    Samples = [0.25f, -0.25f],
                    StartTime = TimeSpan.Zero,
                    EndTime = TimeSpan.FromMilliseconds(125),
                    SampleRate = 16_000,
                }));

        var (server, services, options) = await StartServerAsync(
            wakeWordDetector,
            sttEngine,
            vadEngine);
        var (clientOne, writerOne, parserOne) = await ConnectClientAsync(options);
        var (clientTwo, writerTwo, parserTwo) = await ConnectClientAsync(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await WaitUntilAsync(
                () => server.ActiveSessionCount == 2,
                "Expected two concurrent sessions after both clients connected.");

            await Task.WhenAll(
                SendWakeListeningAudioAsync(writerOne, cts.Token),
                SendWakeListeningAudioAsync(writerTwo, cts.Token));

            var responses = await Task.WhenAll(
                parserOne.ReadEventAsync(cts.Token),
                parserTwo.ReadEventAsync(cts.Token));

            Assert.All(responses, response => Assert.IsType<NotDetectedEvent>(response));

            await Task.WhenAll(
                writerOne.WriteEventAsync(new DescribeEvent(), cts.Token),
                writerTwo.WriteEventAsync(new DescribeEvent(), cts.Token));

            var infoResponses = await Task.WhenAll(
                parserOne.ReadEventAsync(cts.Token),
                parserTwo.ReadEventAsync(cts.Token));

            Assert.All(infoResponses, response => Assert.IsType<InfoEvent>(response));
        }
        finally
        {
            clientOne.Close();
            clientTwo.Close();
            clientOne.Dispose();
            clientTwo.Dispose();

            await WaitUntilAsync(
                () => server.ActiveSessionCount == 0,
                "Expected concurrent sessions to be cleaned up after both clients disconnected.");

            await StopServerAsync(server, services);
        }
    }

    private static WyomingOptions CreateOptions()
    {
        return new WyomingOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = GetAvailablePort(),
            ReadTimeoutSeconds = 5,
        };
    }

    private static async Task<(
        WyomingServer Server,
        ServiceProvider Services,
        WyomingOptions Options)> StartServerAsync(
        IWakeWordDetector? wakeWordDetector = null,
        ISttEngine? sttEngine = null,
        IVadEngine? vadEngine = null)
    {
        return await StartServerAsync(CreateOptions(), wakeWordDetector, sttEngine, vadEngine);
    }

    private static async Task<(
        WyomingServer Server,
        ServiceProvider Services,
        WyomingOptions Options)> StartServerAsync(
        WyomingOptions options,
        IWakeWordDetector? wakeWordDetector = null,
        ISttEngine? sttEngine = null,
        IVadEngine? vadEngine = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<WyomingOptions>>(Options.Create(options));

        if (wakeWordDetector is not null)
        {
            services.AddSingleton(wakeWordDetector);
            services.AddSingleton<IWakeWordDetector>(wakeWordDetector);
        }

        if (sttEngine is not null)
        {
            services.AddSingleton(sttEngine);
            services.AddSingleton<ISttEngine>(sttEngine);
        }

        if (vadEngine is not null)
        {
            services.AddSingleton(vadEngine);
            services.AddSingleton<IVadEngine>(vadEngine);
        }

        var serviceProvider = services.BuildServiceProvider();
        var server = new WyomingServer(Options.Create(options), serviceProvider, NullLogger<WyomingServer>.Instance);
        await server.StartAsync(CancellationToken.None);

        return (server, serviceProvider, options);
    }

    private static async Task<(TcpClient Client, WyomingEventWriter Writer, WyomingEventParser Parser)> ConnectClientAsync(
        WyomingOptions options)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(options.Host), options.Port);

        var stream = client.GetStream();
        return (client, new WyomingEventWriter(stream), new WyomingEventParser(stream, options));
    }

    private static async Task StopServerAsync(WyomingServer server, ServiceProvider services)
    {
        await server.StopAsync(CancellationToken.None);
        server.Dispose();
        await services.DisposeAsync();
    }

    private static async Task SendWakeListeningAudioAsync(WyomingEventWriter writer, CancellationToken ct)
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
        await writer.WriteEventAsync(new AudioStopEvent(), ct);
    }

    private static async Task WriteRawHeaderAsync(TcpClient client, string header, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes($"{header}\n");
        var stream = client.GetStream();
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        string failureMessage,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(condition(), failureMessage);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
