using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using lucia.Agents.Models;

using StackExchange.Redis;

namespace lucia.Agents.Services;

/// <summary>
/// Redis-backed implementation of <see cref="IDeviceCacheService"/>.
/// Stores device entities without embeddings; callers must re-attach embeddings after retrieval.
/// </summary>
public sealed class RedisDeviceCacheService : IDeviceCacheService
{
    private const string LightsKey = "lucia:cache:lights";
    private const string PlayersKey = "lucia:cache:players";
    private const string EmbeddingKeyPrefix = "lucia:cache:embed:";
    private const string AreaEmbeddingsKey = "lucia:cache:area-embeds";

    private static readonly ActivitySource ActivitySource = new("Lucia.Services.DeviceCache", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Services.DeviceCache", "1.0.0");

    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("device.cache.hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("device.cache.misses");
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>("device.cache.operation.duration", "ms");

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDeviceCacheService> _logger;

    public RedisDeviceCacheService(IConnectionMultiplexer redis, ILogger<RedisDeviceCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<List<LightEntity>?> GetCachedLightsAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetCachedLights");
        var start = Stopwatch.GetTimestamp();

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(LightsKey);

            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1);
                return null;
            }

            CacheHits.Add(1);
            var dtos = JsonSerializer.Deserialize<List<LightCacheDto>>((string)value!);
            if (dtos is null) return null;

            return dtos.Select(d => new LightEntity
            {
                EntityId = d.EntityId,
                FriendlyName = d.FriendlyName,
                SupportedColorModes = (SupportedColorModes)d.SupportedColorModes,
                Area = d.Area
            }).ToList();
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error retrieving cached lights");
            return null;
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task SetCachedLightsAsync(List<LightEntity> lights, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SetCachedLights");
        var start = Stopwatch.GetTimestamp();

        try
        {
            var dtos = lights.Select(l => new LightCacheDto(
                l.EntityId,
                l.FriendlyName,
                (int)l.SupportedColorModes,
                l.Area)).ToList();

            var json = JsonSerializer.Serialize(dtos);
            var db = _redis.GetDatabase();
            await db.StringSetAsync(LightsKey, json, ttl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error setting cached lights");
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task<List<MusicPlayerEntity>?> GetCachedPlayersAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetCachedPlayers");
        var start = Stopwatch.GetTimestamp();

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(PlayersKey);

            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1);
                return null;
            }

            CacheHits.Add(1);
            var dtos = JsonSerializer.Deserialize<List<PlayerCacheDto>>((string)value!);
            if (dtos is null) return null;

            return dtos.Select(d => new MusicPlayerEntity
            {
                EntityId = d.EntityId,
                FriendlyName = d.FriendlyName,
                ConfigEntryId = d.ConfigEntryId,
                IsSatellite = d.IsSatellite
            }).ToList();
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error retrieving cached players");
            return null;
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task SetCachedPlayersAsync(List<MusicPlayerEntity> players, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SetCachedPlayers");
        var start = Stopwatch.GetTimestamp();

        try
        {
            var dtos = players.Select(p => new PlayerCacheDto(
                p.EntityId,
                p.FriendlyName,
                p.ConfigEntryId,
                p.IsSatellite)).ToList();

            var json = JsonSerializer.Serialize(dtos);
            var db = _redis.GetDatabase();
            await db.StringSetAsync(PlayersKey, json, ttl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error setting cached players");
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task<Embedding<float>?> GetEmbeddingAsync(string key, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetEmbedding");
        activity?.SetTag("cache.key", key);
        var start = Stopwatch.GetTimestamp();

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(EmbeddingKeyPrefix + key);

            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1);
                return null;
            }

            CacheHits.Add(1);
            var array = JsonSerializer.Deserialize<float[]>((string)value!);
            return array is null ? null : new Embedding<float>(array);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error retrieving embedding for key {Key}", key);
            return null;
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task SetEmbeddingAsync(string key, Embedding<float> embedding, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SetEmbedding");
        activity?.SetTag("cache.key", key);
        var start = Stopwatch.GetTimestamp();

        try
        {
            var array = embedding.Vector.ToArray();
            var json = JsonSerializer.Serialize(array);
            var db = _redis.GetDatabase();
            await db.StringSetAsync(EmbeddingKeyPrefix + key, json, ttl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error setting embedding for key {Key}", key);
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task<Dictionary<string, Embedding<float>>?> GetAreaEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GetAreaEmbeddings");
        var start = Stopwatch.GetTimestamp();

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(AreaEmbeddingsKey);

            if (value.IsNullOrEmpty)
            {
                CacheMisses.Add(1);
                return null;
            }

            CacheHits.Add(1);
            var raw = JsonSerializer.Deserialize<Dictionary<string, float[]>>((string)value!);
            if (raw is null) return null;

            return raw.ToDictionary(
                kvp => kvp.Key,
                kvp => new Embedding<float>(kvp.Value));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error retrieving area embeddings");
            return null;
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task SetAreaEmbeddingsAsync(Dictionary<string, Embedding<float>> areaEmbeddings, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("SetAreaEmbeddings");
        var start = Stopwatch.GetTimestamp();

        try
        {
            var raw = areaEmbeddings.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Vector.ToArray());

            var json = JsonSerializer.Serialize(raw);
            var db = _redis.GetDatabase();
            await db.StringSetAsync(AreaEmbeddingsKey, json, ttl);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis error setting area embeddings");
        }
        finally
        {
            OperationDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private sealed record LightCacheDto(string EntityId, string FriendlyName, int SupportedColorModes, string? Area);

    private sealed record PlayerCacheDto(string EntityId, string FriendlyName, string? ConfigEntryId, bool IsSatellite);
}
