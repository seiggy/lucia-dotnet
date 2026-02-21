using Microsoft.Extensions.AI;
using lucia.Agents.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Provides Redis-backed caching for device state and embeddings.
/// </summary>
public interface IDeviceCacheService
{
    Task<List<LightEntity>?> GetCachedLightsAsync(CancellationToken cancellationToken = default);
    Task SetCachedLightsAsync(List<LightEntity> lights, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<List<MusicPlayerEntity>?> GetCachedPlayersAsync(CancellationToken cancellationToken = default);
    Task SetCachedPlayersAsync(List<MusicPlayerEntity> players, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<Embedding<float>?> GetEmbeddingAsync(string key, CancellationToken cancellationToken = default);
    Task SetEmbeddingAsync(string key, Embedding<float> embedding, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<Dictionary<string, Embedding<float>>?> GetAreaEmbeddingsAsync(CancellationToken cancellationToken = default);
    Task SetAreaEmbeddingsAsync(Dictionary<string, Embedding<float>> areaEmbeddings, TimeSpan ttl, CancellationToken cancellationToken = default);
}
