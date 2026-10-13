using lucia.Agents;
using System.Text.Json;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Cached decorator for ISpeakerProfileStore. Reads enrolled profiles from an
/// <see cref="IKeyValueCache"/> (in-memory or Redis) instead of the database. The cache is
/// invalidated on writes and entries expire after a fixed TTL.
/// </summary>
public sealed class CachedSpeakerProfileStore : ISpeakerProfileStore, IConditionalSpeakerProfileStore
{
    private const string EnrolledCacheKey = "lucia:speaker-profiles:enrolled";
    private const string ProfileKeyPrefix = "lucia:speaker-profiles:id:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ISpeakerProfileStore _inner;
    private readonly IKeyValueCache _cache;
    private readonly ILogger<CachedSpeakerProfileStore> _logger;

    public CachedSpeakerProfileStore(
        ISpeakerProfileStore inner,
        IKeyValueCache cache,
        ILogger<CachedSpeakerProfileStore> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetAsync(EnrolledCacheKey, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                var profiles = JsonSerializer.Deserialize<List<SpeakerProfile>>(cached, JsonOptions);
                if (profiles is not null)
                    return profiles;
            }
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogDebug(ex, "Cache miss for enrolled profiles, falling back to store");
        }

        var result = await _inner.GetEnrolledProfilesAsync(ct).ConfigureAwait(false);

        // Cache in background — don't block the caller
        _ = Task.Run(async () =>
        {
            try
            {
                var json = JsonSerializer.Serialize(result, JsonOptions);
                await _cache.SetAsync(EnrolledCacheKey, json, CacheTtl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to cache enrolled profiles");
            }
        }, CancellationToken.None);

        return result;
    }

    public async Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetAsync(ProfileKeyPrefix + id, ct).ConfigureAwait(false);
            if (cached is not null)
                return JsonSerializer.Deserialize<SpeakerProfile>(cached, JsonOptions);
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogDebug(ex, "Speaker profile cache read failed, falling back to store");
        }

        return await _inner.GetAsync(id, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetAllAsync(CancellationToken ct) =>
        _inner.GetAllAsync(ct);

    public Task<IReadOnlyList<SpeakerProfile>> GetProvisionalProfilesAsync(CancellationToken ct) =>
        _inner.GetProvisionalProfilesAsync(ct);

    public async Task CreateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        await _inner.CreateAsync(profile, ct).ConfigureAwait(false);
        await InvalidateCacheAsync().ConfigureAwait(false);
    }

    public async Task UpdateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        await _inner.UpdateAsync(profile, ct).ConfigureAwait(false);
        await InvalidateCacheAsync().ConfigureAwait(false);
    }

    public async Task<SpeakerProfile?> UpdateAtomicAsync(
        string id, Func<SpeakerProfile, SpeakerProfile> transform, CancellationToken ct)
    {
        var result = await _inner.UpdateAtomicAsync(id, transform, ct).ConfigureAwait(false);
        await InvalidateCacheAsync().ConfigureAwait(false);
        return result;
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        await _inner.DeleteAsync(id, ct).ConfigureAwait(false);
        await InvalidateCacheAsync().ConfigureAwait(false);
    }

    public async Task<bool> DeleteExpiredProvisionalAsync(
        string id,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        if (_inner is not IConditionalSpeakerProfileStore conditionalStore)
        {
            return false;
        }

        var deleted = await conditionalStore.DeleteExpiredProvisionalAsync(id, cutoff, ct).ConfigureAwait(false);
        if (deleted)
        {
            await InvalidateCacheAsync().ConfigureAwait(false);
        }

        return deleted;
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(
        int retentionDays, CancellationToken ct) =>
        _inner.GetExpiredProvisionalProfilesAsync(retentionDays, ct);

    private async Task InvalidateCacheAsync()
    {
        try
        {
            await _cache.RemoveAsync(EnrolledCacheKey).ConfigureAwait(false);
        }
        // Nothing to propagate here: this has no caller token, and failing would only break best-effort work.
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to invalidate enrolled profiles cache");
        }
    }
}
