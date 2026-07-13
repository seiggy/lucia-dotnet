using System.Net.Sockets;
using FakeItEasy;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Wyoming;

public sealed class WyomingServerShutdownTests
{
    [Fact]
    public async Task StopAsync_GracefulSessionExitPrecedesDisposal()
    {
        var sttSession = new GatedTestSttSession();
        var (server, services, options, _, _) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        var stopTask = server.StopAsync(CancellationToken.None);

        Assert.False(stopTask.IsCompleted);
        Assert.Equal(0, sttSession.DisposeCount);

        sttSession.Unblock();
        await stopTask;

        Assert.Equal(1, sttSession.DisposeCount);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task StopAsync_WaitsForInFlightSession()
    {
        var sttSession = new GatedTestSttSession();
        var (server, services, options, _, _) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        var stopTask = server.StopAsync(CancellationToken.None);
        Assert.False(stopTask.IsCompleted);

        sttSession.Unblock();
        await stopTask;

        Assert.Equal(0, server.TrackedSessionTaskCount);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task StopAsync_DrainTimeoutForcesSessionCleanup()
    {
        var timeProvider = new FakeTimeProvider();
        var sttSession = new GatedTestSttSession();
        var eventBus = new SessionEventBus();
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var disconnected = WaitForEventAsync<SessionDisconnectedEvent>(eventBus, testTimeout.Token);
        var (server, services, options, _, _) = await StartServerAsync(sttSession, timeProvider, eventBus);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        var stopTask = server.StopAsync(CancellationToken.None);
        Assert.False(stopTask.IsCompleted);

        while (!server.IsDrainStarted)
        {
            testTimeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        await stopTask.WaitAsync(testTimeout.Token);

        Assert.Equal(1, sttSession.DisposeCount);
        Assert.Equal(1, server.TrackedSessionTaskCount);

        sttSession.Unblock();
        await disconnected;
        Assert.Equal(0, server.TrackedSessionTaskCount);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task NaturalCompletion_RemovesTrackedSessionTask()
    {
        var eventBus = new SessionEventBus();
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var disconnected = WaitForEventAsync<SessionDisconnectedEvent>(eventBus, testTimeout.Token);
        var (server, services, options, _, _) = await StartServerAsync(
            new TestSttSession(new SttResult()),
            eventBus: eventBus);
        var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port, testTimeout.Token);

        client.Dispose();
        await disconnected;

        Assert.Equal(0, server.TrackedSessionTaskCount);
        await server.StopAsync(CancellationToken.None);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task UnexpectedSessionFault_IsObservedAndLoggedOnce()
    {
        var eventBus = new SessionEventBus();
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var disconnected = WaitForEventAsync<SessionDisconnectedEvent>(eventBus, testTimeout.Token);
        var (server, services, options, serverLogger, _) = await StartServerAsync(
            new ThrowingTestSttSession(),
            eventBus: eventBus);
        using var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port, testTimeout.Token);
        var writer = new WyomingEventWriter(client.GetStream());

        await WriteAudioAsync(writer, testTimeout.Token);
        await disconnected;

        Assert.Equal(
            1,
            CountLogs(serverLogger, LogLevel.Warning, "terminated with error"));
        await server.StopAsync(CancellationToken.None);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task StopAsync_CallerCancellationIsPreservedWithoutShutdownErrorLogs()
    {
        var sttSession = new GatedTestSttSession();
        var (server, services, options, serverLogger, sessionLogger) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => server.StopAsync(callerCancellation.Token));

        sttSession.Unblock();
        await server.StopAsync(CancellationToken.None);

        Assert.Equal(0, CountLogs(serverLogger, LogLevel.Warning, "terminated with error"));
        Assert.Equal(0, CountLogs(sessionLogger, LogLevel.Error, "Unhandled error"));
        Assert.Equal(0, CountLogs(sessionLogger, LogLevel.Information, "I/O ended"));
        await DisposeServerAsync(server, services);
    }

    private static async Task<(
        WyomingServer Server,
        ServiceProvider Services,
        WyomingOptions Options,
        ILogger<WyomingServer> ServerLogger,
        ILogger<WyomingSession> SessionLogger)> StartServerAsync(
        ISttSession sttSession,
        TimeProvider? timeProvider = null,
        SessionEventBus? eventBus = null)
    {
        var options = new WyomingOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = GetAvailablePort(),
            ReadTimeoutSeconds = 30,
        };
        var serverLogger = A.Fake<ILogger<WyomingServer>>();
        var sessionLogger = A.Fake<ILogger<WyomingSession>>();
        var services = new ServiceCollection()
            .AddSingleton<IOptions<WyomingOptions>>(Options.Create(options))
            .AddSingleton<ILogger<WyomingSession>>(sessionLogger)
            .AddSingleton<ISttEngine>(new TestSttEngine(sttSession))
            .BuildServiceProvider();
        var server = new WyomingServer(
            Options.Create(options),
            services,
            serverLogger,
            eventBus ?? new SessionEventBus(),
            timeProvider);

        await server.StartAsync(CancellationToken.None);
        return (server, services, options, serverLogger, sessionLogger);
    }

    private static async Task<TcpClient> ConnectAndStartFinalizationAsync(
        WyomingOptions options,
        GatedTestSttSession sttSession)
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port, testTimeout.Token);
        await WriteAudioAsync(new WyomingEventWriter(client.GetStream()), testTimeout.Token);
        await sttSession.FinalizationStarted.Task.WaitAsync(testTimeout.Token);
        return client;
    }

    private static async Task WriteAudioAsync(WyomingEventWriter writer, CancellationToken ct)
    {
        await writer.WriteEventAsync(
            new AudioStartEvent { Rate = 16_000, Width = 2, Channels = 1 },
            ct);
        await writer.WriteEventAsync(
            new AudioChunkEvent
            {
                Rate = 16_000,
                Width = 2,
                Channels = 1,
                Payload = [0, 0],
            },
            ct);
        await writer.WriteEventAsync(new AudioStopEvent(), ct);
    }

    private static async Task<TEvent> WaitForEventAsync<TEvent>(
        SessionEventBus eventBus,
        CancellationToken ct)
        where TEvent : SessionEvent
    {
        await foreach (var evt in eventBus.SubscribeAsync(ct))
        {
            if (evt is TEvent expected)
            {
                return expected;
            }
        }

        throw new InvalidOperationException($"Event {typeof(TEvent).Name} was not published.");
    }

    private static int CountLogs<T>(
        ILogger<T> logger,
        LogLevel level,
        string message)
    {
        return Fake.GetCalls(logger)
            .Count(call =>
                call.Method.Name == nameof(ILogger.Log)
                && Equals(call.Arguments[0], level)
                && call.Arguments[2]?.ToString()?.Contains(message, StringComparison.Ordinal) is true);
    }

    private static async Task DisposeServerAsync(WyomingServer server, ServiceProvider services)
    {
        server.Dispose();
        await services.DisposeAsync();
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
