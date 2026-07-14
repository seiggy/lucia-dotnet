using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using FakeItEasy;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Wyoming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    public async Task StopAsync_RepeatedAndConcurrentCallsShareShutdown()
    {
        var sttSession = new GatedTestSttSession();
        var (server, services, options, _, _) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        var stopTasks = Enumerable.Range(0, 8)
            .Select(_ => server.StopAsync(CancellationToken.None))
            .ToArray();

        Assert.All(stopTasks, task => Assert.False(task.IsCompleted));
        sttSession.Unblock();
        await Task.WhenAll(stopTasks);

        Assert.Equal(0, server.ActiveSessionCount);
        Assert.Equal(1, sttSession.DisposeCount);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task StopAsync_TimeoutRemovesTrackingAndObservesLateFaultOnce()
    {
        var sttSession = new GatedTestSttSession();
        var eventBus = new SessionEventBus();
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var disconnected = WaitForEventAsync<SessionDisconnectedEvent>(eventBus, testTimeout.Token);
        var (server, services, options, serverLogger, sessionLogger) =
            await StartServerAsync(sttSession, eventBus);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        try
        {
            await server.StopAsync(CancellationToken.None).WaitAsync(testTimeout.Token);

            Assert.Equal(0, server.ActiveSessionCount);
            Assert.Equal(0, sttSession.DisposeCount);
            Assert.False(sttSession.DisposalStarted.Task.IsCompleted);

            sttSession.Fail(new InvalidOperationException("late STT failure"));
            await disconnected;
            await sttSession.DisposalCompleted.Task.WaitAsync(testTimeout.Token);

            Assert.Equal(1, CountLogs(serverLogger, LogLevel.Warning, 18001));
            Assert.Equal(
                1,
                CountExceptionLogs(serverLogger, "late STT failure")
                + CountExceptionLogs(sessionLogger, "late STT failure"));
            Assert.Equal(1, sttSession.DisposeCount);
        }
        finally
        {
            sttSession.Unblock();
            sttSession.UnblockDisposal();
            await DisposeServerAsync(server, services);
        }
    }

    [Fact]
    public async Task StopAsync_SynchronousCancellationCallbackCannotExceedShutdownDeadline()
    {
        var sttSession = new GatedTestSttSession(blockDisposal: true);
        var (server, services, options, serverLogger, _) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);
        var cancellation = GetServerCancellation(server);
        using var callbackRelease = new ManualResetEventSlim();
        var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFaultObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(serverLogger)
            .Where(call =>
                call.Method.Name == nameof(ILogger.Log)
                && Equals(call.Arguments[1], new EventId(18004)))
            .Invokes(_ => lateFaultObserved.TrySetResult());
        using var registration = cancellation.Token.Register(() =>
        {
            callbackStarted.TrySetResult();
            callbackRelease.Wait();
            throw new InvalidOperationException("late cancellation failure");
        });

        var stopwatch = Stopwatch.StartNew();
        var stopTask = Task.Run(() => server.StopAsync(CancellationToken.None));
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await stopTask.WaitAsync(TimeSpan.FromMilliseconds(4_900));
            stopwatch.Stop();
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(4_900),
                $"Shutdown exceeded its total deadline: {stopwatch.Elapsed}.");
        }
        finally
        {
            callbackRelease.Set();
            sttSession.Unblock();
            await sttSession.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            sttSession.UnblockDisposal();
            await sttSession.DisposalCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await lateFaultObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await stopTask;
            Assert.Equal(1, CountLogs(serverLogger, LogLevel.Error, 18004));
            await DisposeServerAsync(server, services);
        }
    }

    [Fact]
    public async Task StopAsync_TimeoutHandsOffBeforeLateCompletionAndDisposesOnce()
    {
        var sttSession = new GatedTestSttSession();
        var (server, services, options, _, _) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        await server.StopAsync(CancellationToken.None);

        Assert.Equal(0, server.ActiveSessionCount);
        Assert.Equal(0, sttSession.DisposeCount);
        Assert.False(sttSession.DisposalStarted.Task.IsCompleted);

        sttSession.Unblock();
        await sttSession.DisposalCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, sttSession.DisposeCount);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task Dispose_BeforeStopAsyncCoordinatesWithoutBlocking()
    {
        var sttSession = new GatedTestSttSession();
        var (server, services, options, _, _) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        server.Dispose();
        var stopTask = server.StopAsync(CancellationToken.None);
        sttSession.Unblock();
        await stopTask;

        Assert.Equal(0, server.ActiveSessionCount);
        Assert.Equal(1, sttSession.DisposeCount);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_DuringStopAsyncSharesShutdown()
    {
        var sttSession = new GatedTestSttSession();
        var (server, services, options, _, _) = await StartServerAsync(sttSession);
        using var client = await ConnectAndStartFinalizationAsync(options, sttSession);

        var stopTask = server.StopAsync(CancellationToken.None);
        server.Dispose();
        sttSession.Unblock();
        await stopTask;

        Assert.Equal(0, server.ActiveSessionCount);
        Assert.Equal(1, sttSession.DisposeCount);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task AcceptedSessionPublishesConnectedBeforeDisconnected()
    {
        var eventBus = new SessionEventBus();
        var eventsTask = CollectConnectionEventsAsync(eventBus);
        var (server, services, options, _, _) = await StartServerAsync(
            new TestSttSession(new SttResult()),
            eventBus);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(options.Host, options.Port);
        }

        var events = await eventsTask.WaitAsync(TimeSpan.FromSeconds(2));

        var connected = Assert.IsType<SessionConnectedEvent>(events[0]);
        var disconnected = Assert.IsType<SessionDisconnectedEvent>(events[1]);
        Assert.Equal(connected.SessionId, disconnected.SessionId);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task StartAsync_FailedBindRollsBackAndCanRetry()
    {
        using var occupiedListener = new TcpListener(IPAddress.Loopback, 0);
        occupiedListener.Start();
        var options = new WyomingOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = ((IPEndPoint)occupiedListener.LocalEndpoint).Port,
            ReadTimeoutSeconds = 30,
        };
        var (server, services, _, _, _) = CreateServer(
            new TestSttSession(new SttResult()),
            options: options);

        await Assert.ThrowsAsync<SocketException>(
            () => server.StartAsync(CancellationToken.None));

        occupiedListener.Stop();
        await server.StartAsync(CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port);

        await server.StopAsync(CancellationToken.None);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task StartAsync_ConcurrentCallsStartOneListenerAndStopIsIdempotent()
    {
        var (server, services, options, _, _) = CreateServer(
            new TestSttSession(new SttResult()));

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => server.StartAsync(CancellationToken.None))));

        using var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port);
        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => server.StopAsync(CancellationToken.None)));

        Assert.Equal(0, server.ActiveSessionCount);
        await DisposeServerAsync(server, services);
    }

    [Fact]
    public async Task StopAsync_AcceptRaceLeavesNoSessionsAndRejectsNewConnections()
    {
        var (server, services, options, _, _) = await StartServerAsync(
            new TestSttSession(new SttResult()));
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clients = Enumerable.Range(0, 16).Select(_ => new TcpClient()).ToArray();

        try
        {
            var connections = clients
                .Select(client => client.ConnectAsync(options.Host, options.Port, testTimeout.Token).AsTask())
                .ToArray();
            var stopTask = server.StopAsync(CancellationToken.None);

            await Task.WhenAll(connections.Select(IgnoreConnectionFailureAsync));
            await stopTask.WaitAsync(testTimeout.Token);

            Assert.Equal(0, server.ActiveSessionCount);
            using var rejectedClient = new TcpClient();
            await Assert.ThrowsAnyAsync<SocketException>(
                async () => await rejectedClient.ConnectAsync(
                    options.Host,
                    options.Port,
                    testTimeout.Token));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }

            await DisposeServerAsync(server, services);
        }
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

        Assert.Equal(0, CountLogs(serverLogger, LogLevel.Warning, 18001));
        Assert.Equal(0, CountExceptionLogs(sessionLogger, "late STT failure"));
        await DisposeServerAsync(server, services);
    }

    private static async Task<(
        WyomingServer Server,
        ServiceProvider Services,
        WyomingOptions Options,
        ILogger<WyomingServer> ServerLogger,
        ILogger<WyomingSession> SessionLogger)> StartServerAsync(
        ISttSession sttSession,
        SessionEventBus? eventBus = null)
    {
        var result = CreateServer(sttSession, eventBus);
        await result.Server.StartAsync(CancellationToken.None);
        return result;
    }

    private static (
        WyomingServer Server,
        ServiceProvider Services,
        WyomingOptions Options,
        ILogger<WyomingServer> ServerLogger,
        ILogger<WyomingSession> SessionLogger) CreateServer(
        ISttSession sttSession,
        SessionEventBus? eventBus = null,
        WyomingOptions? options = null)
    {
        options ??= new WyomingOptions
        {
            Host = IPAddress.Loopback.ToString(),
            Port = GetAvailablePort(),
            ReadTimeoutSeconds = 30,
        };
        var serverLogger = A.Fake<ILogger<WyomingServer>>();
        var sessionLogger = A.Fake<ILogger<WyomingSession>>();
        A.CallTo(() => serverLogger.IsEnabled(A<LogLevel>._)).Returns(true);
        A.CallTo(() => sessionLogger.IsEnabled(A<LogLevel>._)).Returns(true);
        var services = new ServiceCollection()
            .AddSingleton<IOptions<WyomingOptions>>(Options.Create(options))
            .AddSingleton<ILogger<WyomingSession>>(sessionLogger)
            .AddSingleton<ISttEngine>(new TestSttEngine(sttSession))
            .BuildServiceProvider();
        var server = new WyomingServer(
            Options.Create(options),
            services,
            serverLogger,
            eventBus ?? new SessionEventBus());

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

    private static async Task<SessionEvent[]> CollectConnectionEventsAsync(SessionEventBus eventBus)
    {
        var events = new List<SessionEvent>(2);
        await foreach (var evt in eventBus.SubscribeAsync())
        {
            if (evt is not (SessionConnectedEvent or SessionDisconnectedEvent))
            {
                continue;
            }

            events.Add(evt);
            if (events.Count == 2)
            {
                return [.. events];
            }
        }

        throw new InvalidOperationException("The connection event stream ended early.");
    }

    private static CancellationTokenSource GetServerCancellation(WyomingServer server)
    {
        var field = typeof(WyomingServer).GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WyomingServer cancellation field was not found.");
        return (CancellationTokenSource?)field.GetValue(server)
            ?? throw new InvalidOperationException("WyomingServer has not started.");
    }

    private static int CountLogs<T>(
        ILogger<T> logger,
        LogLevel level,
        int eventId)
    {
        return Fake.GetCalls(logger)
            .Count(call =>
                call.Method.Name == nameof(ILogger.Log)
                && Equals(call.Arguments[0], level)
                && call.Arguments[1] is EventId actualEventId
                && actualEventId.Id == eventId);
    }

    private static int CountExceptionLogs<T>(ILogger<T> logger, string message)
    {
        return Fake.GetCalls(logger)
            .Count(call =>
                call.Method.Name == nameof(ILogger.Log)
                && call.Arguments[3] is Exception exception
                && exception.Message.Contains(message, StringComparison.Ordinal));
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

    private static async Task IgnoreConnectionFailureAsync(Task connection)
    {
        try
        {
            await connection;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return;
        }
    }
}
