using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Redis-cached decorator for ISpeakerProfileStore. Reads enrolled profiles from
/// Redis cache (~1ms) instead of MongoDB (~500ms+). Cache is refreshed on writes
/// and expires after a configurable TTL.
/// </summary>
public sealed class CachedSpeakerProfileStore : ISpeakerProfileStore
{
    private const string EnrolledCacheKey = "lucia:speaker-profiles:enrolled";
    private const string ProfileKeyPrefix = "lucia:speaker-profiles:id:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ISpeakerProfileStore _inner;
    private readonly IDatabase _redis;
    private readonly ILogger<CachedSpeakerProfileStore> _logger;

    public CachedSpeakerProfileStore(
        ISpeakerProfileStore inner,
        IConnectionMultiplexer redis,
        ILogger<CachedSpeakerProfileStore> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct)
    {
        try
        {
            var cached = await _redis.StringGetAsync(EnrolledCacheKey).ConfigureAwait(false);
            if (cached.HasValue)
            {
                var profiles = JsonSerializer.Deserialize<List<SpeakerProfile>>(cached.ToString(), JsonOptions);
                if (profiles is not null)
                    return profiles;
            }
        }
        catch (RedisException ex)
        {
            _logger.LogDebug(ex, "Redis cache miss for enrolled profiles, falling back to store");
        }

        var result = await _inner.GetEnrolledProfilesAsync(ct).ConfigureAwait(false);

        // Cache in background — don't block the caller
        _ = Task.Run(async () =>
        {
            try
            {
                var json = JsonSerializer.Serialize(result, JsonOptions);
                await _redis.StringSetAsync(EnrolledCacheKey, json, CacheTtl).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to cache enrolled profiles in Redis");
            }
        }, CancellationToken.None);

        return result;
    }

    public async Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct)
    {
        try
        {
            var cached = await _redis.StringGetAsync(ProfileKeyPrefix + id).ConfigureAwait(false);
            if (cached.HasValue)
                return JsonSerializer.Deserialize<SpeakerProfile>(cached.ToString(), JsonOptions);
        }
        catch (RedisException) { /* fall through */ }

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

    public Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(
        int retentionDays, CancellationToken ct) =>
        _inner.GetExpiredProvisionalProfilesAsync(retentionDays, ct);

    private async Task InvalidateCacheAsync()
    {
        try
        {
            await _redis.KeyDeleteAsync(EnrolledCacheKey).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogDebug(ex, "Failed to invalidate enrolled profiles cache");
        }
    }
}
