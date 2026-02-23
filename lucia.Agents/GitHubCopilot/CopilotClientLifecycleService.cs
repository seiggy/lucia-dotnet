using GitHub.Copilot.SDK;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.GitHubCopilot;

/// <summary>
/// Manages the shared <see cref="CopilotClient"/> process lifecycle.
/// Starts the CLI when at least one <see cref="ProviderType.GitHubCopilot"/> provider
/// is configured, and shuts it down gracefully on host stop.
/// </summary>
public sealed class CopilotClientLifecycleService : IHostedService, IAsyncDisposable
{
    private readonly IModelProviderRepository _providerRepository;
    private readonly ILogger<CopilotClientLifecycleService> _logger;
    private CopilotClient? _client;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// The shared <see cref="CopilotClient"/> instance, or <c>null</c> if no
    /// Copilot provider is configured or the client failed to start.
    /// </summary>
    public CopilotClient? Client => _client;

    public CopilotClientLifecycleService(
        IModelProviderRepository providerRepository,
        ILogger<CopilotClientLifecycleService> logger)
    {
        _providerRepository = providerRepository;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var providers = await _providerRepository.GetEnabledProvidersAsync(cancellationToken).ConfigureAwait(false);
        var copilotProvider = providers.FirstOrDefault(p => p.ProviderType == ProviderType.GitHubCopilot);

        if (copilotProvider is null)
        {
            _logger.LogInformation("No enabled GitHub Copilot provider found — skipping CLI startup");
            return;
        }

        await StartClientAsync(copilotProvider, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeClientAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the Copilot CLI process is running. Called lazily when a Copilot
    /// provider is first used at runtime (hot-reload scenario where provider is
    /// added after startup).
    /// </summary>
    public async Task EnsureStartedAsync(ModelProvider provider, CancellationToken cancellationToken)
    {
        if (_client is not null) return;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null) return;
            await StartClientAsync(provider, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task StartClientAsync(ModelProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            var options = new CopilotClientOptions();

            if (!string.IsNullOrWhiteSpace(provider.Auth?.ApiKey))
            {
                options.GithubToken = provider.Auth.ApiKey;
            }

            var client = new CopilotClient(options);
            await client.StartAsync(cancellationToken).ConfigureAwait(false);

            _client = client;
            _logger.LogInformation("GitHub Copilot CLI started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start GitHub Copilot CLI — Copilot agents will be unavailable");
        }
    }

    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;

        if (client is null) return;

        try
        {
            await client.StopAsync().ConfigureAwait(false);
            _logger.LogInformation("GitHub Copilot CLI stopped");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping GitHub Copilot CLI");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
