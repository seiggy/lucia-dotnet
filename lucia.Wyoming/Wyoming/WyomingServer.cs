using System.Collections.Concurrent;
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
    private readonly Lazy<Task> _shutdownTask;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private int _isStopping;

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
        _shutdownTask = new Lazy<Task>(StopCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int ActiveSessionCount => _sessions.Count;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_isStopping != 0 || _listener is not null)
            {
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Parse(_options.Host), _options.Port);
            _listener.Start();

            _logger.LogInformation("Wyoming server listening on {Host}:{Port}", _options.Host, _options.Port);
            _acceptLoopTask = AcceptConnectionsAsync(_cts.Token);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _shutdownTask.Value.WaitAsync(cancellationToken);

    private async Task StopCoreAsync()
    {
        var forcedCleanupTimeout = ShutdownTimeout / 5;
        TcpListener? listener;
        CancellationTokenSource? cts;

        lock (_lifecycleLock)
        {
            _isStopping = 1;
            listener = _listener;
            cts = _cts;
        }

        try
        {
            _logger.LogInformation("Wyoming server shutting down, draining {Count} sessions", _sessions.Count);

            listener?.Stop();
            cts?.Cancel();

            await DrainAsync()
                .WaitAsync(ShutdownTimeout - forcedCleanupTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogDrainTimeout(_logger, ShutdownTimeout, _sessions.Count);
            await WaitForForcedCleanupAsync(
                    ForceCleanupSessions(),
                    forcedCleanupTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogShutdownFailure(_logger, ex);
            await WaitForForcedCleanupAsync(
                    ForceCleanupSessions(),
                    forcedCleanupTimeout)
                .ConfigureAwait(false);
        }
        finally
        {
            cts?.Dispose();
            listener?.Stop();
            _sttConcurrency.Dispose();
            _ttsConcurrency.Dispose();
        }
    }

    private async Task DrainAsync()
    {
        if (_acceptLoopTask is not null)
        {
            await _acceptLoopTask.ConfigureAwait(false);
        }

        var sessionTasks = _sessions.Keys.ToArray();
        if (sessionTasks.Length > 0)
        {
            await Task.WhenAll(sessionTasks).ConfigureAwait(false);
        }
    }

    private Task[] ForceCleanupSessions()
    {
        var cleanupTasks = new List<Task>();
        foreach (var entry in _sessions.ToArray())
        {
            if (!_sessions.TryRemove(entry.Key, out var session))
            {
                continue;
            }

            cleanupTasks.Add(Task.Run(() => DisposeSession(session)));
        }

        return cleanupTasks.ToArray();
    }

    private static async Task WaitForForcedCleanupAsync(
        Task[] cleanupTasks,
        TimeSpan timeout)
    {
        if (cleanupTasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(cleanupTasks)
                .WaitAsync(timeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return;
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);

                WyomingSession? session = null;
                lock (_lifecycleLock)
                {
                    if (_isStopping == 0
                        && !ct.IsCancellationRequested
                        && _sessions.Count < _options.MaxWakeWordStreams)
                    {
                        session = CreateSession(client);
                        TrackSession(session, ct);
                    }
                }

                if (session is null)
                {
                    if (_isStopping == 0 && !ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "Maximum wake word streams ({Max}) reached, rejecting connection",
                            _options.MaxWakeWordStreams);
                    }

                    client.Dispose();
                    if (_isStopping != 0 || ct.IsCancellationRequested)
                    {
                        break;
                    }

                    continue;
                }

                _eventBus.Publish(new SessionConnectedEvent
                {
                    SessionId = session.Id,
                    RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown",
                });

                _logger.LogInformation(
                    "New Wyoming session {SessionId} from {RemoteEndPoint}",
                    session.Id,
                    client.Client.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                client?.Dispose();
                break;
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException or SocketException
                && (Volatile.Read(ref _isStopping) != 0 || ct.IsCancellationRequested))
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

    private void TrackSession(WyomingSession session, CancellationToken ct)
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

                PublishDisconnected(session.Id);
            }
        }

        trackedTask = RunAsync();
        _sessions.TryAdd(trackedTask, session);
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

    private void PublishDisconnected(string sessionId) =>
        _eventBus.Publish(new SessionDisconnectedEvent { SessionId = sessionId });

    public void Dispose()
    {
        _ = _shutdownTask.Value;
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
