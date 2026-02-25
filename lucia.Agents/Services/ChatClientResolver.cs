using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Resolves an <see cref="IChatClient"/> from the MongoDB-backed model provider system.
/// Caches resolved clients by provider ID to avoid repeated construction.
/// Falls back to the "default-chat" built-in provider when no provider name is specified.
/// </summary>
public sealed class ChatClientResolver : IChatClientResolver
{
    private const string DefaultChatProviderId = "default-chat";

    private readonly IModelProviderRepository _repository;
    private readonly IModelProviderResolver _resolver;
    private readonly ILogger<ChatClientResolver> _logger;
    private readonly Dictionary<string, IChatClient> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ChatClientResolver(
        IModelProviderRepository repository,
        IModelProviderResolver resolver,
        ILogger<ChatClientResolver> logger)
    {
        _repository = repository;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<IChatClient> ResolveAsync(string? providerName, CancellationToken ct = default)
    {
        // When no provider is specified, fall back to the built-in default
        var effectiveName = string.IsNullOrWhiteSpace(providerName) ? DefaultChatProviderId : providerName;

        // Check cache first
        lock (_lock)
        {
            if (_cache.TryGetValue(effectiveName, out var cached))
                return cached;
        }

        var provider = await _repository.GetProviderAsync(effectiveName, ct).ConfigureAwait(false);

        // If the named provider doesn't exist and we were looking for the default,
        // fall back to any enabled chat provider so agents work regardless of user-chosen IDs.
        if ((provider is null || !provider.Enabled)
            && string.Equals(effectiveName, DefaultChatProviderId, StringComparison.OrdinalIgnoreCase))
        {
            var all = await _repository.GetEnabledProvidersAsync(ct).ConfigureAwait(false);
            provider = all.FirstOrDefault(p => p.Purpose == Configuration.ModelPurpose.Chat);
            if (provider is not null)
            {
                _logger.LogInformation(
                    "Default chat provider not found — falling back to '{ProviderId}' ({ProviderType})",
                    provider.Id, provider.ProviderType);
            }
        }

        if (provider is null || !provider.Enabled)
        {
            _logger.LogWarning(
                "Model provider '{ProviderName}' not found or disabled",
                effectiveName);
            throw new InvalidOperationException(
                $"Model provider '{effectiveName}' not found or disabled. " +
                "Configure a default chat model provider in the dashboard.");
        }
        if (provider.ProviderType == ProviderType.GitHubCopilot)
        {
            _logger.LogWarning(
                "Provider '{ProviderName}' is GitHubCopilot (AIAgent-only). " +
                "Falling back to default chat provider for IChatClient callers.",
                effectiveName);
            if (!string.Equals(effectiveName, DefaultChatProviderId, StringComparison.OrdinalIgnoreCase))
                return await ResolveAsync(DefaultChatProviderId, ct).ConfigureAwait(false);

            throw new InvalidOperationException(
                "Default chat provider is a GitHubCopilot type which cannot produce an IChatClient. " +
                "Configure a non-Copilot default chat provider.");
        }

        try
        {
            var client = _resolver.CreateClient(provider);

            lock (_lock)
            {
                _cache[effectiveName] = client;
            }

            _logger.LogInformation(
                "Resolved chat client from provider '{ProviderName}' ({ProviderType}/{Model})",
                effectiveName, provider.ProviderType, provider.ModelName);

            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create client for provider '{ProviderName}'",
                effectiveName);
            throw;
        }
    }

    public async Task<AIAgent?> ResolveAIAgentAsync(string? providerName, CancellationToken ct = default)
    {
        var effectiveName = string.IsNullOrWhiteSpace(providerName) ? DefaultChatProviderId : providerName;

        var provider = await _repository.GetProviderAsync(effectiveName, ct).ConfigureAwait(false);

        if (provider is null || !provider.Enabled)
            return null;

        if (provider.ProviderType != ProviderType.GitHubCopilot)
            return null;

        var agent = await _resolver.CreateAIAgentAsync(provider, ct).ConfigureAwait(false);
        if (agent is null) return agent;
        return agent
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();
    }
}
