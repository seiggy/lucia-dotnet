using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Wyoming;

public sealed class WyomingServer : IHostedService, IDisposable
{
    // ponytail: fixed 5-second drain ceiling; use HostOptions only if real session cleanup regularly exceeds it.
    private const int SessionDrainTimeoutSeconds = 5;
    private readonly WyomingOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WyomingServer> _logger;
    private readonly SessionEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, WyomingSession> _sessions = new();
    private readonly ConcurrentDictionary<string, Task> _sessionTasks = new();
    private readonly SemaphoreSlim _sttConcurrency;
    private readonly SemaphoreSlim _ttsConcurrency;
    private readonly object _lifecycleLock = new();
    private readonly Lazy<Task> _stopTask;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private int _lifecycleState;
    private int _drainStarted;
    private int _disposed;

    public WyomingServer(
        IOptions<WyomingOptions> options,
        IServiceProvider serviceProvider,
        ILogger<WyomingServer> logger,
        SessionEventBus eventBus,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(eventBus);

        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _eventBus = eventBus;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sttConcurrency = new SemaphoreSlim(_options.MaxConcurrentSttSessions);
        _ttsConcurrency = new SemaphoreSlim(_options.MaxConcurrentTtsSyntheses);
        _stopTask = new Lazy<Task>(StopCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int ActiveSessionCount => _sessions.Count;

    internal int TrackedSessionTaskCount => _sessionTasks.Count;

    internal bool IsDrainStarted => Volatile.Read(ref _drainStarted) != 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_lifecycleState != 0)
            {
                return Task.CompletedTask;
            }

            _lifecycleState = 1;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listener = new TcpListener(IPAddress.Parse(_options.Host), _options.Port);
            _listener.Start();

            _logger.LogInformation("Wyoming server listening on {Host}:{Port}", _options.Host, _options.Port);
            _acceptLoopTask = AcceptConnectionsAsync(_cts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopTask.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCoreAsync()
    {
        Task? acceptLoopTask;
        TcpListener? listener;
        CancellationTokenSource? cts;

        lock (_lifecycleLock)
        {
            _lifecycleState = 2;
            acceptLoopTask = _acceptLoopTask;
            listener = _listener;
            cts = _cts;
        }

        _logger.LogInformation("Wyoming server shutting down, draining {Count} sessions", _sessions.Count);

        listener?.Stop();
        cts?.Cancel();

        if (acceptLoopTask is not null)
        {
            await acceptLoopTask.ConfigureAwait(false);
        }

        var sessionTasks = _sessionTasks.ToArray();
        if (sessionTasks.Length == 0)
        {
            return;
        }

        try
        {
            var drainTimeout = TimeSpan.FromSeconds(SessionDrainTimeoutSeconds);
            var drainTask = Task.WhenAll(sessionTasks.Select(entry => entry.Value))
                .WaitAsync(drainTimeout, _timeProvider, CancellationToken.None);
            Volatile.Write(ref _drainStarted, 1);
            await drainTask.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            var remaining = sessionTasks
                .Where(entry => !entry.Value.IsCompleted)
                .Select(entry => entry.Key)
                .ToArray();

            _logger.LogWarning(
                "Wyoming shutdown drain timed out after {Timeout}; forcing cleanup of {Count} sessions",
                TimeSpan.FromSeconds(SessionDrainTimeoutSeconds),
                remaining.Length);

            foreach (var sessionId in remaining)
            {
                if (_sessions.TryGetValue(sessionId, out var session))
                {
                    try
                    {
                        session.ForceDispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Forced cleanup failed for Wyoming session {SessionId}", sessionId);
                    }
                }
            }
        }
        catch (Exception)
        {
            _logger.LogDebug("Wyoming session drain completed after an observed session fault");
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                {
                    client.Dispose();
                    break;
                }

                if (_sessions.Count >= _options.MaxWakeWordStreams)
                {
                    _logger.LogWarning(
                        "Maximum wake word streams ({Max}) reached, rejecting connection",
                        _options.MaxWakeWordStreams);
                    client.Dispose();
                    continue;
                }

                var session = CreateSession(client);
                _sessions.TryAdd(session.Id, session);
                var sessionTask = RunSessionAsync(session, ct);
                _sessionTasks.TryAdd(session.Id, sessionTask);

                _eventBus.Publish(new SessionConnectedEvent
                {
                    SessionId = session.Id,
                    RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown",
                });

                _logger.LogInformation(
                    "New Wyoming session {SessionId} from {RemoteEndPoint}",
                    session.Id,
                    client.Client.RemoteEndPoint);

                _ = ObserveSessionAsync(session, sessionTask, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException or SocketException
                && (ct.IsCancellationRequested || Volatile.Read(ref _lifecycleState) == 2))
            {
                break;
            }
            catch (Exception ex)
            {
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

    private WyomingSession CreateSession(TcpClient client)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<WyomingSession>>();
        return new WyomingSession(client, _serviceProvider, logger, _options, _eventBus, _sttConcurrency);
    }

    private async Task RunSessionAsync(WyomingSession session, CancellationToken ct)
    {
        try
        {
            await session.RunAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            session.Dispose();
            _sessions.TryRemove(session.Id, out _);
            _logger.LogInformation("Wyoming session {SessionId} ended", session.Id);
        }
    }

    private async Task ObserveSessionAsync(
        WyomingSession session,
        Task sessionTask,
        CancellationToken ct)
    {
        try
        {
            await sessionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Wyoming session {SessionId} task cancelled during shutdown", session.Id);
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Wyoming session {SessionId} task disposed during shutdown", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wyoming session {SessionId} terminated with error", session.Id);
        }
        finally
        {
            _sessionTasks.TryRemove(session.Id, out _);
            _eventBus.Publish(new SessionDisconnectedEvent { SessionId = session.Id });
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts?.Dispose();
        _listener?.Stop();
        _sttConcurrency.Dispose();
        _ttsConcurrency.Dispose();
    }
}
