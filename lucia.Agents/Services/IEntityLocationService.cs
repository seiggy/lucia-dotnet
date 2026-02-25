using lucia.Agents.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Provides centralized, cached entity location resolution across floors, areas, and entities.
/// Shared as a singleton across all skills in both AgentHost and A2AHost.
/// Data is cached in Redis with a 24h TTL and held in thread-safe immutable in-memory collections.
/// </summary>
public interface IEntityLocationService
{
    /// <summary>
    /// Eager-load all location data from Home Assistant and cache to Redis.
    /// Called during startup. Safe to call concurrently — only one load runs at a time.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Invalidate Redis cache and reload from Home Assistant.
    /// </summary>
    Task InvalidateAndReloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all cached floors.
    /// </summary>
    Task<IReadOnlyList<FloorInfo>> GetFloorsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all cached areas.
    /// </summary>
    Task<IReadOnlyList<AreaInfo>> GetAreasAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all cached entity location metadata.
    /// </summary>
    Task<IReadOnlyList<EntityLocationInfo>> GetEntitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Find entities by logical location name using a search cascade:
    /// exact area name → area alias → floor name → floor alias → substring → embeddings (≥ 0.90).
    /// Returns entity metadata (no state) for all entities in matched location(s).
    /// </summary>
    Task<IReadOnlyList<EntityLocationInfo>> FindEntitiesByLocationAsync(
        string locationName,
        CancellationToken ct = default);

    /// <summary>
    /// Find entities by location, filtered to specific domains (e.g., "light", "climate").
    /// </summary>
    Task<IReadOnlyList<EntityLocationInfo>> FindEntitiesByLocationAsync(
        string locationName,
        IReadOnlyList<string> domainFilter,
        CancellationToken ct = default);

    /// <summary>
    /// Get the area for an entity (0..1 relationship). Returns null if no area assigned.
    /// Thread-safe: reads from immutable snapshot.
    /// </summary>
    AreaInfo? GetAreaForEntity(string entityId);

    /// <summary>
    /// Get the floor for an area (0..1 relationship). Returns null if no floor assigned.
    /// Thread-safe: reads from immutable snapshot.
    /// </summary>
    FloorInfo? GetFloorForArea(string areaId);

    /// <summary>
    /// When location data was last successfully loaded from Home Assistant.
    /// </summary>
    DateTimeOffset? LastLoadedAt { get; }
}
