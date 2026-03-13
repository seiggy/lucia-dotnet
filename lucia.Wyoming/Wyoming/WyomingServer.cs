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
    private readonly WyomingOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WyomingServer> _logger;
    private readonly ConcurrentDictionary<string, WyomingSession> _sessions = new();
    private readonly SemaphoreSlim _sttConcurrency;
    private readonly SemaphoreSlim _ttsConcurrency;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public WyomingServer(
        IOptions<WyomingOptions> options,
        IServiceProvider serviceProvider,
        ILogger<WyomingServer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _sttConcurrency = new SemaphoreSlim(_options.MaxConcurrentSttSessions);
        _ttsConcurrency = new SemaphoreSlim(_options.MaxConcurrentTtsSyntheses);
    }

    public int ActiveSessionCount => _sessions.Count;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Parse(_options.Host), _options.Port);
        _listener.Start();

        _logger.LogInformation("Wyoming server listening on {Host}:{Port}", _options.Host, _options.Port);

        _ = AcceptConnectionsAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Wyoming server shutting down, closing {Count} sessions", _sessions.Count);
        _cts?.Cancel();
        _listener?.Stop();

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }

        _sessions.Clear();
        await Task.CompletedTask;
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);

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

                _logger.LogInformation(
                    "New Wyoming session {SessionId} from {RemoteEndPoint}",
                    session.Id,
                    client.Client.RemoteEndPoint);

                _ = RunSessionAsync(session, ct);
            }
            catch (OperationCanceledException)
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
        return new WyomingSession(client, _serviceProvider, logger, _options);
    }

    private async Task RunSessionAsync(WyomingSession session, CancellationToken ct)
    {
        try
        {
            await session.RunAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Wyoming session {SessionId} terminated with error", session.Id);
            session.Dispose();
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            session.Dispose();
            _logger.LogInformation("Wyoming session {SessionId} ended", session.Id);
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _listener?.Stop();
        _sttConcurrency.Dispose();
        _ttsConcurrency.Dispose();
    }
}
