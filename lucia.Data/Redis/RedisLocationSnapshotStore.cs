using System.Collections.Immutable;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace lucia.Data.Redis;

/// <summary>Stores the location snapshot in Redis with a 24 hour expiry and a shared version counter.</summary>
public sealed class RedisLocationSnapshotStore : ILocationSnapshotStore
{
    private const string FloorsKey = "lucia:location:floors";
    private const string AreasKey = "lucia:location:areas";
    private const string EntitiesKey = "lucia:location:entities";
    private const string LoadedAtKey = "lucia:location:loaded-at";
    private const string VersionKey = "lucia:location:version";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisLocationSnapshotStore> _logger;

    public RedisLocationSnapshotStore(IConnectionMultiplexer redis, ILogger<RedisLocationSnapshotStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<StoredLocationSnapshot?> TryLoadAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();

            var floorsJson = db.StringGetSetExpiryAsync(FloorsKey, CacheTtl);
            var areasJson = db.StringGetSetExpiryAsync(AreasKey, CacheTtl);
            var entitiesJson = db.StringGetSetExpiryAsync(EntitiesKey, CacheTtl);
            var loadedAtStr = db.StringGetSetExpiryAsync(LoadedAtKey, CacheTtl);
            var versionVal = db.StringGetSetExpiryAsync(VersionKey, CacheTtl);

            await Task.WhenAll(floorsJson, areasJson, entitiesJson, loadedAtStr, versionVal)
                .ConfigureAwait(false);

            var floorsResult = await floorsJson.ConfigureAwait(false);
            var areasResult = await areasJson.ConfigureAwait(false);
            var entitiesResult = await entitiesJson.ConfigureAwait(false);
            var loadedAtResult = await loadedAtStr.ConfigureAwait(false);
            var versionResult = await versionVal.ConfigureAwait(false);

            if (floorsResult.IsNullOrEmpty || areasResult.IsNullOrEmpty || entitiesResult.IsNullOrEmpty)
                return null;

            var floors = JsonSerializer.Deserialize<FloorInfo[]>((string)floorsResult!)
                ?.ToImmutableArray() ?? ImmutableArray<FloorInfo>.Empty;
            var areas = JsonSerializer.Deserialize<AreaInfo[]>((string)areasResult!)
                ?.ToImmutableArray() ?? ImmutableArray<AreaInfo>.Empty;
            var entities = JsonSerializer.Deserialize<HomeAssistantEntity[]>((string)entitiesResult!)
                ?.ToImmutableArray() ?? ImmutableArray<HomeAssistantEntity>.Empty;

            DateTimeOffset? loadedAt = !loadedAtResult.IsNullOrEmpty
                && DateTimeOffset.TryParse((string)loadedAtResult!, out var parsed)
                    ? parsed
                    : null;
            long? version = versionResult.TryParse(out long ver) ? ver : null;

            return new StoredLocationSnapshot(LocationSnapshot.Create(floors, areas, entities), loadedAt, version);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to load location data from Redis, will load from Home Assistant");
            return null;
        }
    }

    public async Task SaveAsync(LocationSnapshot snapshot, DateTimeOffset? loadedAt, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();

            var floorsJson = JsonSerializer.Serialize(snapshot.Floors);
            var areasJson = JsonSerializer.Serialize(snapshot.Areas);
            var entitiesJson = JsonSerializer.Serialize(snapshot.Entities);

            var batch = db.CreateBatch();
            var tasks = new List<Task>
            {
                batch.StringSetAsync(FloorsKey, floorsJson, CacheTtl),
                batch.StringSetAsync(AreasKey, areasJson, CacheTtl),
                batch.StringSetAsync(EntitiesKey, entitiesJson, CacheTtl),
                batch.StringSetAsync(LoadedAtKey, loadedAt?.ToString("O") ?? "", CacheTtl)
            };
            batch.Execute();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            _logger.LogDebug("Saved entity location data to Redis");
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to save location data to Redis; the snapshot version must not be published");
            throw;
        }
    }

    public async Task InvalidateAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync([
                (RedisKey)FloorsKey, (RedisKey)AreasKey, (RedisKey)EntitiesKey,
                (RedisKey)LoadedAtKey
            ]).ConfigureAwait(false);

            _logger.LogInformation("Invalidated entity location cache, reloading from Home Assistant");
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to clear Redis location cache, proceeding with reload");
        }
    }

    public async Task<long?> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var versionVal = await db.StringGetSetExpiryAsync(VersionKey, CacheTtl).ConfigureAwait(false);
            return versionVal.TryParse(out long version) ? version : null;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to check Redis location cache version, using current in-memory data");
            return null;
        }
    }

    public async Task<long?> BumpVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var newVersion = await db.StringIncrementAsync(VersionKey).ConfigureAwait(false);
            _logger.LogDebug("Bumped Redis location cache version to {Version}", newVersion);
            return newVersion;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to bump Redis location cache version");
            return null;
        }
    }
}
