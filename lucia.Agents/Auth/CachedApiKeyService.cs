using System.Text.Json;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Auth;

/// <summary>
/// Decorator that caches <see cref="IApiKeyService.ValidateKeyAsync"/> results in an
/// <see cref="IKeyValueCache"/> (in-memory or Redis) to avoid a database round-trip on every
/// authenticated request. Every mutation moves the cache to a new generation, which orphans
/// all earlier entries.
/// </summary>
public sealed class CachedApiKeyService : IApiKeyService
{
    private const string GenerationKey = "lucia:api-keys:generation";
    private const string EntryKeyPrefix = "lucia:api-keys:entry:";

    // Outlives any entry, so a generation can never be forgotten while its entries are alive.
    private static readonly TimeSpan GenerationTimeToLive = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IApiKeyService _inner;
    private readonly IKeyValueCache _cache;
    private readonly ILogger<CachedApiKeyService> _logger;
    private readonly TimeSpan _cacheDuration;
    private long _bypassUntilTickCount;

    public CachedApiKeyService(
        IApiKeyService inner,
        IKeyValueCache cache,
        ILogger<CachedApiKeyService> logger,
        TimeSpan? cacheDuration = null)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
    }

    public async Task<ApiKeyEntry?> ValidateKeyAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return null;
        }

        // After a failed invalidation the cache may still hold keys that were just revoked.
        if (Environment.TickCount64 < Volatile.Read(ref _bypassUntilTickCount))
        {
            return await _inner.ValidateKeyAsync(plaintextKey, cancellationToken).ConfigureAwait(false);
        }

        string? entryKey = null;
        try
        {
            var generation = await _cache.GetAsync(GenerationKey, cancellationToken).ConfigureAwait(false) ?? "0";
            entryKey = $"{EntryKeyPrefix}{generation}:{HashKey(plaintextKey)}";

            var cached = await _cache.GetAsync(entryKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<ApiKeyEntry>(cached, JsonOptions);
            }
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogDebug(ex, "API key cache read failed; validating against the store");
        }

        var entry = await _inner.ValidateKeyAsync(plaintextKey, cancellationToken).ConfigureAwait(false);

        // Invalid keys are not cached. Each one would add an entry to a shared cache that any caller can fill.
        if (entryKey is not null && entry is not null)
        {
            try
            {
                var payload = JsonSerializer.Serialize(entry, JsonOptions);
                await _cache.SetAsync(entryKey, payload, _cacheDuration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ex.IsCancellation())
            {
                _logger.LogDebug(ex, "API key cache write failed");
            }
        }

        return entry;
    }
    public async Task<ApiKeyCreateResponse> CreateKeyAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        // Creating a key needs no invalidation. Only invalid keys could go stale, and they are not cached.
        return await _inner.CreateKeyAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiKeyCreateResponse> CreateAdministratorKeyAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return await _inner.CreateAdministratorKeyAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiKeyCreateResponse?> CreateAdministratorKeyIfNoneAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return await _inner.CreateAdministratorKeyIfNoneAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiKeyCreateResponse?> CreateKeyFromPlaintextAsync(string name, string plaintextKey, CancellationToken cancellationToken = default)
    {
        return await _inner.CreateKeyFromPlaintextAsync(name, plaintextKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var result = await _inner.RevokeKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        if (result)
        {
            await InvalidateAllAsync().ConfigureAwait(false);
        }
        return result;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var result = await _inner.RegenerateKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        await InvalidateAllAsync().ConfigureAwait(false);
        return result;
    }

    // Pass-through methods that don't benefit from caching
    public Task<IReadOnlyList<ApiKeySummary>> ListKeysAsync(CancellationToken cancellationToken = default)
        => _inner.ListKeysAsync(cancellationToken);

    public Task<ApiKeySummary?> GetKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default)
        => _inner.GetKeyAsync(keyId, cancellationToken);

    public Task<int> GetActiveKeyCountAsync(CancellationToken cancellationToken = default)
        => _inner.GetActiveKeyCountAsync(cancellationToken);

    public Task<bool> HasAnyKeysAsync(CancellationToken cancellationToken = default)
        => _inner.HasAnyKeysAsync(cancellationToken);

    public async Task<(ApiKeyCreateResponse? Created, int RevokedCount)> OverrideKeyFromPlaintextAsync(
        string name,
        string plaintextKey,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner
            .OverrideKeyFromPlaintextAsync(
                name,
                plaintextKey,
                cancellationToken)
            .ConfigureAwait(false);
        // Invalidate on any valid override attempt: a revoke-only path (Created == null) can still
        // mean a previously-cached valid key must no longer be accepted.
        await InvalidateAllAsync().ConfigureAwait(false);
        return result;
    }

    public async Task<(ApiKeyCreateResponse? Created, int RevokedCount)>
        OverrideAdministratorKeyFromPlaintextAsync(
            string name,
            string plaintextKey,
            CancellationToken cancellationToken = default)
    {
        var result = await _inner
            .OverrideAdministratorKeyFromPlaintextAsync(
                name,
                plaintextKey,
                cancellationToken)
            .ConfigureAwait(false);
        await InvalidateAllAsync().ConfigureAwait(false);
        return result;
    }

    private async Task InvalidateAllAsync()
    {
        try
        {
            await _cache.SetAsync(GenerationKey, Guid.NewGuid().ToString("N"), GenerationTimeToLive).ConfigureAwait(false);
            _logger.LogInformation("API key validation cache invalidated");
        }
        // Nothing to propagate here: this has no caller token, and failing would only break best-effort work.
        catch (Exception ex)
        {
            // Cached entries may still accept a revoked key. Validate against the store until they have
            // aged out. Other instances sharing the cache cannot learn this, so they keep serving the
            // old entries for up to the cache duration.
            Volatile.Write(ref _bypassUntilTickCount, Environment.TickCount64 + (long)_cacheDuration.TotalMilliseconds);
            _logger.LogWarning(
                ex,
                "Failed to invalidate the API key validation cache. Validating against the store for {Duration}",
                _cacheDuration);
        }
    }

    private static string HashKey(string plaintextKey)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(plaintextKey));
        return Convert.ToHexStringLower(bytes);
    }
}
