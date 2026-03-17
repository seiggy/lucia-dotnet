using System.Collections.Concurrent;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Auth;

/// <summary>
/// Decorator that caches <see cref="IApiKeyService.ValidateKeyAsync"/> results in memory
/// to avoid a MongoDB round-trip on every authenticated request. The cache is invalidated
/// automatically when keys are created, revoked, or regenerated.
/// </summary>
public sealed class CachedApiKeyService : IApiKeyService
{
    private readonly IApiKeyService _inner;
    private readonly ILogger<CachedApiKeyService> _logger;
    private readonly TimeSpan _cacheDuration;

    /// <summary>
    /// Keyed by the SHA-256 hash of the plaintext key (same hash the inner service computes).
    /// Value is the cached entry (null means "known invalid key").
    /// </summary>
    private readonly ConcurrentDictionary<string, CacheEntry> _validationCache = new();

    /// <summary>
    /// Monotonically increasing version; bumped on any mutation to force stale reads
    /// to re-validate on their next TTL expiry.
    /// </summary>
    private volatile int _generation;

    public CachedApiKeyService(
        IApiKeyService inner,
        ILogger<CachedApiKeyService> logger,
        TimeSpan? cacheDuration = null)
    {
        _inner = inner;
        _logger = logger;
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
    }

    public async Task<ApiKeyEntry?> ValidateKeyAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return null;
        }

        var cacheKey = HashKey(plaintextKey);
        var now = DateTimeOffset.UtcNow;

        if (_validationCache.TryGetValue(cacheKey, out var cached)
            && cached.Generation == _generation
            && now - cached.CachedAt < _cacheDuration)
        {
            return cached.Entry;
        }

        var entry = await _inner.ValidateKeyAsync(plaintextKey, cancellationToken).ConfigureAwait(false);

        _validationCache[cacheKey] = new CacheEntry(entry, now, _generation);

        if (entry is not null)
        {
            _logger.LogDebug("Cached API key validation for prefix {Prefix}", entry.KeyPrefix);
        }

        return entry;
    }

    public async Task<ApiKeyCreateResponse> CreateKeyAsync(string name, CancellationToken cancellationToken = default)
    {
        var result = await _inner.CreateKeyAsync(name, cancellationToken).ConfigureAwait(false);
        InvalidateAll();
        return result;
    }

    public async Task<ApiKeyCreateResponse?> CreateKeyFromPlaintextAsync(string name, string plaintextKey, CancellationToken cancellationToken = default)
    {
        var result = await _inner.CreateKeyFromPlaintextAsync(name, plaintextKey, cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            InvalidateAll();
        }
        return result;
    }

    public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var result = await _inner.RevokeKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        if (result)
        {
            InvalidateAll();
        }
        return result;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var result = await _inner.RegenerateKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        InvalidateAll();
        return result;
    }

    // Pass-through methods that don't benefit from caching
    public Task<IReadOnlyList<ApiKeySummary>> ListKeysAsync(CancellationToken cancellationToken = default)
        => _inner.ListKeysAsync(cancellationToken);

    public Task<int> GetActiveKeyCountAsync(CancellationToken cancellationToken = default)
        => _inner.GetActiveKeyCountAsync(cancellationToken);

    public Task<bool> HasAnyKeysAsync(CancellationToken cancellationToken = default)
        => _inner.HasAnyKeysAsync(cancellationToken);

    private void InvalidateAll()
    {
        Interlocked.Increment(ref _generation);
        _validationCache.Clear();
        _logger.LogInformation("API key validation cache invalidated");
    }

    private static string HashKey(string plaintextKey)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(plaintextKey));
        return Convert.ToHexStringLower(bytes);
    }

    private sealed record CacheEntry(ApiKeyEntry? Entry, DateTimeOffset CachedAt, int Generation);
}
