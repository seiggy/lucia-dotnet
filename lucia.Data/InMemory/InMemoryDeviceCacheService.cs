using System.Collections.Concurrent;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Data.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IDeviceCacheService"/> using <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Replaces the Redis-backed version for lightweight/mono-container deployments.
/// </summary>
public sealed class InMemoryDeviceCacheService : IDeviceCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry<List<LightEntity>>> _lights = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<MusicPlayerEntity>>> _players = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<ClimateEntity>>> _climateDevices = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<FanEntity>>> _fans = new();
    private readonly ConcurrentDictionary<string, CacheEntry<List<SensorEntity>>> _sensors = new();
    private readonly ConcurrentDictionary<string, CacheEntry<Embedding<float>>> _embeddings = new();
    private readonly ConcurrentDictionary<string, CacheEntry<Dictionary<string, Embedding<float>>>> _areaEmbeddings = new();

    private readonly ILogger<InMemoryDeviceCacheService> _logger;

    public InMemoryDeviceCacheService(ILogger<InMemoryDeviceCacheService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Lights ──────────────────────────────────────────────────────────

    public Task<List<LightEntity>?> GetCachedLightsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetOrExpire(_lights, "lights"));

    public Task SetCachedLightsAsync(List<LightEntity> lights, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _lights["lights"] = new CacheEntry<List<LightEntity>>(lights, ttl);
        return Task.CompletedTask;
    }

    // ── Players ─────────────────────────────────────────────────────────

    public Task<List<MusicPlayerEntity>?> GetCachedPlayersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetOrExpire(_players, "players"));

    public Task SetCachedPlayersAsync(List<MusicPlayerEntity> players, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _players["players"] = new CacheEntry<List<MusicPlayerEntity>>(players, ttl);
        return Task.CompletedTask;
    }

    // ── Climate ─────────────────────────────────────────────────────────

    public Task<List<ClimateEntity>?> GetCachedClimateDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetOrExpire(_climateDevices, "climate"));

    public Task SetCachedClimateDevicesAsync(List<ClimateEntity> devices, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _climateDevices["climate"] = new CacheEntry<List<ClimateEntity>>(devices, ttl);
        return Task.CompletedTask;
    }

    // ── Fans ────────────────────────────────────────────────────────────

    public Task<List<FanEntity>?> GetCachedFansAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetOrExpire(_fans, "fans"));

    public Task SetCachedFansAsync(List<FanEntity> fans, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _fans["fans"] = new CacheEntry<List<FanEntity>>(fans, ttl);
        return Task.CompletedTask;
    }

    // ── Sensors ─────────────────────────────────────────────────────────

    public Task<List<SensorEntity>?> GetCachedSensorsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetOrExpire(_sensors, "sensors"));

    public Task SetCachedSensorsAsync(List<SensorEntity> sensors, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _sensors["sensors"] = new CacheEntry<List<SensorEntity>>(sensors, ttl);
        return Task.CompletedTask;
    }

    // ── Individual Embeddings ───────────────────────────────────────────

    public Task<Embedding<float>?> GetEmbeddingAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(GetOrExpire(_embeddings, key));

    public Task SetEmbeddingAsync(string key, Embedding<float> embedding, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _embeddings[key] = new CacheEntry<Embedding<float>>(embedding, ttl);
        return Task.CompletedTask;
    }

    // ── Area Embeddings ─────────────────────────────────────────────────

    public Task<Dictionary<string, Embedding<float>>?> GetAreaEmbeddingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetOrExpire(_areaEmbeddings, "area-embeds"));

    public Task SetAreaEmbeddingsAsync(Dictionary<string, Embedding<float>> areaEmbeddings, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _areaEmbeddings["area-embeds"] = new CacheEntry<Dictionary<string, Embedding<float>>>(areaEmbeddings, ttl);
        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static T? GetOrExpire<T>(ConcurrentDictionary<string, CacheEntry<T>> store, string key)
        where T : class
    {
        if (!store.TryGetValue(key, out var entry))
            return null;

        if (entry.IsExpired)
        {
            store.TryRemove(key, out _);
            return null;
        }

        return entry.Value;
    }
}
