using lucia.Agents.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Resolves an <see cref="IChatClient"/> from the MongoDB-backed model provider system.
/// Caches resolved clients by provider ID to avoid repeated construction.
/// Falls back to the default DI-registered <see cref="IChatClient"/> when no provider is configured.
/// </summary>
public sealed class ChatClientResolver : IChatClientResolver
{
    private readonly IModelProviderRepository _repository;
    private readonly IModelProviderResolver _resolver;
    private readonly IChatClient _defaultClient;
    private readonly ILogger<ChatClientResolver> _logger;
    private readonly Dictionary<string, IChatClient> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ChatClientResolver(
        IModelProviderRepository repository,
        IModelProviderResolver resolver,
        IChatClient defaultClient,
        ILogger<ChatClientResolver> logger)
    {
        _repository = repository;
        _resolver = resolver;
        _defaultClient = defaultClient;
        _logger = logger;
    }

    public async Task<IChatClient> ResolveAsync(string? providerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return _defaultClient;

        // Check cache first
        lock (_lock)
        {
            if (_cache.TryGetValue(providerName, out var cached))
                return cached;
        }

        var provider = await _repository.GetProviderAsync(providerName, ct).ConfigureAwait(false);

        if (provider is null || !provider.Enabled)
        {
            _logger.LogWarning(
                "Model provider '{ProviderName}' not found or disabled, using default client",
                providerName);
            return _defaultClient;
        }

        try
        {
            var client = _resolver.CreateClient(provider);

            lock (_lock)
            {
                _cache[providerName] = client;
            }

            _logger.LogInformation(
                "Resolved chat client from provider '{ProviderName}' ({ProviderType}/{Model})",
                providerName, provider.ProviderType, provider.ModelName);

            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create client for provider '{ProviderName}', falling back to default",
                providerName);
            return _defaultClient;
        }
    }
}
