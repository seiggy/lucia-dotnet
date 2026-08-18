using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Wyoming;

public sealed partial class WyomingServer : IHostedService, IDisposable
{
    // ponytail: five seconds is the total shutdown ceiling; make it configurable only if production data requires it.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly WyomingOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WyomingServer> _logger;
    private readonly SessionEventBus _eventBus;
    private readonly ConcurrentDictionary<Task, WyomingSession> _sessions = new();
    private readonly SemaphoreSlim _sttConcurrency;
    private readonly SemaphoreSlim _ttsConcurrency;
    private readonly object _lifecycleLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private Task? _shutdownTask;

    public WyomingServer(
        IOptions<WyomingOptions> options,
        IServiceProvider serviceProvider,
        ILogger<WyomingServer> logger,
        SessionEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(eventBus);

        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _eventBus = eventBus;
        _sttConcurrency = new SemaphoreSlim(_options.MaxConcurrentSttSessions);
        _ttsConcurrency = new SemaphoreSlim(_options.MaxConcurrentTtsSyntheses);
    }

    public int ActiveSessionCount => _sessions.Count;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cts = null;
        TcpListener? listener = null;

        try
        {
            lock (_lifecycleLock)
            {
                if (_listener is not null || _shutdownTask is not null)
                {
                    return Task.CompletedTask;
                }

                cancellationToken.ThrowIfCancellationRequested();
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                listener = new TcpListener(IPAddress.Parse(_options.Host), _options.Port);

                listener.Start();
                var acceptLoopTask = AcceptConnectionsAsync(listener, cts.Token);
                _logger.LogInformation(
                    "Wyoming server listening on {Host}:{Port}",
                    _options.Host,
                    _options.Port);

                _cts = cts;
                _listener = listener;
                _acceptLoopTask = acceptLoopTask;
            }
        }
        catch
        {
            listener?.Stop();
            cts?.Cancel();
            cts?.Dispose();
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task shutdownTask;
        lock (_lifecycleLock)
        {
            _shutdownTask ??= StopCoreAsync();
            shutdownTask = _shutdownTask;
        }

        return shutdownTask.WaitAsync(cancellationToken);
    }

    private async Task StopCoreAsync()
    {
        await Task.Yield();

        var startedAt = Stopwatch.GetTimestamp();
        var gracefulTimeout = TimeSpan.FromTicks(ShutdownTimeout.Ticks * 4 / 5);
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? acceptLoopTask;

        lock (_lifecycleLock)
        {
            listener = _listener;
            cts = _cts;
            acceptLoopTask = _acceptLoopTask;
        }

        _logger.LogInformation("Wyoming server shutting down, draining {Count} sessions", _sessions.Count);
        listener?.Stop();
        var cancellationTask = cts?.CancelAsync() ?? Task.CompletedTask;
        var drainTask = DrainAsync(cancellationTask, acceptLoopTask, _sessions.Keys.ToArray());

        try
        {
            await drainTask
                .WaitAsync(Remaining(startedAt, gracefulTimeout))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogDrainTimeout(_logger, gracefulTimeout, _sessions.Count);
            var lateSessions = RemoveSessions();
            var cleanupTask = AbortSessionsAsync(lateSessions);
            if (cts is not null)
            {
                ObserveLateShutdown(drainTask, cleanupTask, lateSessions, cts);
                cts = null;
            }

            if (!cleanupTask.IsCompleted)
            {
                try
                {
                    await cleanupTask
                        .WaitAsync(Remaining(startedAt, ShutdownTimeout))
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogShutdownFailure(_logger, ex);
            foreach (var entry in RemoveSessions())
            {
                DisposeSession(entry.Value);
            }
        }
        finally
        {
            cts?.Dispose();

            _sttConcurrency.Dispose();
            _ttsConcurrency.Dispose();

            lock (_lifecycleLock)
            {
                _listener = null;
                _cts = null;
                _acceptLoopTask = null;
            }
        }
    }

    private static Task DrainAsync(
        Task cancellationTask,
        Task? acceptLoopTask,
        Task[] sessionTasks)
    {
        var tasks = new List<Task>(sessionTasks.Length + 2)
        {
            cancellationTask,
        };
        if (acceptLoopTask is not null)
        {
            tasks.Add(acceptLoopTask);
        }

        tasks.AddRange(sessionTasks);
        return Task.WhenAll(tasks);
    }

    private KeyValuePair<Task, WyomingSession>[] RemoveSessions()
    {
        var removed = new List<KeyValuePair<Task, WyomingSession>>();
        foreach (var entry in _sessions.ToArray())
        {
            if (_sessions.TryRemove(entry.Key, out var session))
            {
                removed.Add(new KeyValuePair<Task, WyomingSession>(entry.Key, session));
            }
        }

        return [.. removed];
    }

    private Task AbortSessionsAsync(KeyValuePair<Task, WyomingSession>[] sessions)
    {
        return Task.WhenAll(sessions.Select(entry => Task.Run(() =>
        {
            try
            {
                entry.Value.AbortTransport();
            }
            catch (Exception ex)
            {
                LogSessionCleanupFailure(_logger, entry.Value.Id, ex);
            }
        })));
    }

    private void ObserveLateShutdown(
        Task drainTask,
        Task cleanupTask,
        KeyValuePair<Task, WyomingSession>[] sessions,
        CancellationTokenSource cts)
    {
        async Task ObserveAsync()
        {
            try
            {
                await cleanupTask.ConfigureAwait(false);
                await drainTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogShutdownFailure(_logger, ex);
            }
            finally
            {
                foreach (var entry in sessions)
                {
                    DisposeSession(entry.Value);
                }

                cts.Dispose();
            }
        }

        _ = ObserveAsync();
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                WyomingSession? session = null;

                lock (_lifecycleLock)
                {
                    if (_shutdownTask is null
                        && !ct.IsCancellationRequested
                        && _sessions.Count < _options.MaxWakeWordStreams)
                    {
                        session = CreateSession(client);
                        TrackSession(session, remoteEndPoint, ct);
                        client = null;
                    }
                }

                if (session is null)
                {
                    if (_shutdownTask is null && !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "Maximum wake word streams ({Max}) reached, rejecting connection",
                            _options.MaxWakeWordStreams);
                    }

                    client?.Dispose();
                    if (_shutdownTask is not null || ct.IsCancellationRequested)
                    {
                        break;
                    }

                    continue;
                }

                _logger.LogInformation(
                    "New Wyoming session {SessionId} from {RemoteEndPoint}",
                    session.Id,
                    remoteEndPoint);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException or SocketException
                && (_shutdownTask is not null || ct.IsCancellationRequested))
            {
                client?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                client?.Dispose();
                _logger.LogError(ex, "Error accepting Wyoming connection");

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void TrackSession(
        WyomingSession session,
        string remoteEndPoint,
        CancellationToken ct)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? trackedTask = null;

        async Task RunAsync()
        {
            await start.Task.ConfigureAwait(false);

            try
            {
                await session.RunAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogSessionFailure(_logger, session.Id, ex);
            }
            finally
            {
                if (_sessions.TryRemove(trackedTask!, out var ownedSession))
                {
                    DisposeSession(ownedSession);
                }

                _eventBus.Publish(new SessionDisconnectedEvent { SessionId = session.Id });
            }
        }

        trackedTask = RunAsync();
        if (!_sessions.TryAdd(trackedTask, session))
        {
            throw new InvalidOperationException("Unable to track Wyoming session.");
        }

        _eventBus.Publish(new SessionConnectedEvent
        {
            SessionId = session.Id,
            RemoteEndPoint = remoteEndPoint,
        });
        start.SetResult();
    }

    private WyomingSession CreateSession(TcpClient client)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<WyomingSession>>();
        return new WyomingSession(client, _serviceProvider, logger, _options, _eventBus, _sttConcurrency);
    }

    private void DisposeSession(WyomingSession session)
    {
        try
        {
            session.Dispose();
            _logger.LogInformation("Wyoming session {SessionId} ended", session.Id);
        }
        catch (Exception ex)
        {
            LogSessionCleanupFailure(_logger, session.Id, ex);
        }
    }

    private static TimeSpan Remaining(long startedAt, TimeSpan deadline)
    {
        var remaining = deadline - Stopwatch.GetElapsedTime(startedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _shutdownTask ??= StopCoreAsync();
        }
    }

    [LoggerMessage(
        EventId = 18001,
        Level = LogLevel.Warning,
        Message = "Wyoming session {SessionId} terminated with an unexpected error")]
    private static partial void LogSessionFailure(
        ILogger logger,
        string sessionId,
        Exception exception);

    [LoggerMessage(
        EventId = 18002,
        Level = LogLevel.Warning,
        Message = "Cleanup failed for Wyoming session {SessionId}")]
    private static partial void LogSessionCleanupFailure(
        ILogger logger,
        string sessionId,
        Exception exception);

    [LoggerMessage(
        EventId = 18003,
        Level = LogLevel.Warning,
        Message = "Wyoming shutdown reached its {Timeout} timeout; forcing cleanup of {Count} sessions")]
    private static partial void LogDrainTimeout(
        ILogger logger,
        TimeSpan timeout,
        int count);

    [LoggerMessage(
        EventId = 18004,
        Level = LogLevel.Error,
        Message = "Wyoming shutdown failed; forcing cleanup")]
    private static partial void LogShutdownFailure(
        ILogger logger,
        Exception exception);
}
