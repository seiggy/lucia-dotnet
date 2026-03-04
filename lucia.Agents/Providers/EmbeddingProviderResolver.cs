using System.Collections.Concurrent;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Providers;

/// <summary>
/// Resolves <see cref="IEmbeddingGenerator{String, Embedding}"/> instances from
/// the MongoDB-backed <see cref="ModelProvider"/> system. Caches generators by
/// provider ID so repeated calls return the same instance.
/// </summary>
public sealed class EmbeddingProviderResolver : IEmbeddingProviderResolver
{
    private readonly IModelProviderRepository _providerRepository;
    private readonly IModelProviderResolver _providerResolver;
    private readonly ILogger<EmbeddingProviderResolver> _logger;

    /// <summary>
    /// Cache of already-created embedding generators keyed by provider ID.
    /// Thread-safe for concurrent agent initialization.
    /// </summary>
    private readonly ConcurrentDictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _cache = new();

    /// <summary>
    /// Per-provider-ID lock to prevent duplicate generator creation when multiple
    /// callers race on the same provider key.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _creationLocks = new();

    public EmbeddingProviderResolver(
        IModelProviderRepository providerRepository,
        IModelProviderResolver providerResolver,
        ILogger<EmbeddingProviderResolver> logger)
    {
        _providerRepository = providerRepository;
        _providerResolver = providerResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEmbeddingGenerator<string, Embedding<float>>?> ResolveAsync(
        string? providerName = null,
        CancellationToken ct = default)
    {
        // If a specific provider was requested, try that first
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            return await ResolveByIdAsync(providerName, ct).ConfigureAwait(false);
        }

        // Otherwise find the first enabled embedding provider (system default)
        return await ResolveDefaultAsync(ct).ConfigureAwait(false);
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>?> ResolveByIdAsync(
        string providerId, CancellationToken ct)
    {
        if (_cache.TryGetValue(providerId, out var cached))
            return cached;

        var provider = await _providerRepository.GetProviderAsync(providerId, ct).ConfigureAwait(false);
        if (provider is null || !provider.Enabled)
        {
            _logger.LogWarning("Embedding provider '{ProviderId}' not found or disabled", providerId);
            return null;
        }

        if (provider.Purpose != ModelPurpose.Embedding)
        {
            _logger.LogWarning("Provider '{ProviderId}' has purpose {Purpose}, expected Embedding", providerId, provider.Purpose);
            return null;
        }

        return await GetOrCreateAsync(provider.Id, provider).ConfigureAwait(false);
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>?> ResolveDefaultAsync(CancellationToken ct)
    {
        const string defaultKey = "__system_default__";
        if (_cache.TryGetValue(defaultKey, out var cached))
            return cached;

        var providers = await _providerRepository.GetEnabledProvidersAsync(ct).ConfigureAwait(false);
        var embeddingProvider = providers.FirstOrDefault(p => p.Purpose == ModelPurpose.Embedding);

        if (embeddingProvider is null)
        {
            _logger.LogWarning("No enabled embedding provider configured. Skills requiring embeddings will not function.");
            return null;
        }

        var generator = await GetOrCreateAsync(embeddingProvider.Id, embeddingProvider).ConfigureAwait(false);
        // Also cache under the default key for quick lookup
        _cache[defaultKey] = generator;
        return generator;
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>> GetOrCreateAsync(
        string key, ModelProvider provider)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var semaphore = _creationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock
            if (_cache.TryGetValue(key, out cached))
                return cached;

            var generator = _providerResolver.CreateEmbeddingGenerator(provider);
            _cache[key] = generator;
            _logger.LogInformation("Created embedding generator for provider '{ProviderId}' ({ProviderType}, model={Model})",
                provider.Id, provider.ProviderType, provider.ModelName);
            return generator;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create embedding generator for provider '{ProviderId}'", provider.Id);
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
