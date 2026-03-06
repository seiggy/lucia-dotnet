using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Abstractions;

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
    /// Get all cached entities.
    /// </summary>
    Task<IReadOnlyList<HomeAssistantEntity>> GetEntitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Find entities by logical location name using HybridEntityMatcher
    /// against floors and areas, then returning all entities in matched locations.
    /// </summary>
    Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName,
        CancellationToken ct = default);

    /// <summary>
    /// Find entities by location, filtered to specific domains (e.g., "light", "climate").
    /// </summary>
    Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName,
        IReadOnlyList<string>? domainFilter,
        CancellationToken ct = default);

    /// <summary>
    /// Find entities by location with full control over match configuration.
    /// </summary>
    Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName,
        IReadOnlyList<string>? domainFilter,
        HybridMatchOptions hybridMatchOptions,
        CancellationToken ct = default);

    /// <summary>
    /// Hybrid search directly against entities by name/alias.
    /// </summary>
    Task<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>> SearchEntitiesAsync(
        string query,
        IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Hybrid search against areas by name/alias.
    /// </summary>
    Task<IReadOnlyList<EntityMatchResult<AreaInfo>>> SearchAreasAsync(
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Hybrid search against floors by name/alias.
    /// </summary>
    Task<IReadOnlyList<EntityMatchResult<FloorInfo>>> SearchFloorsAsync(
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Debug search that walks the hierarchy (floors → areas → entities) and returns
    /// scored results at every level. If a floor matches, all its areas and their entities
    /// are included. If an area matches, all its entities are included. Direct entity
    /// matches are also returned. Use for troubleshooting the hybrid matcher.
    /// </summary>
    Task<HierarchicalSearchResult> SearchHierarchyAsync(
        string query,
        HybridMatchOptions? options = null,
        IReadOnlyList<string>? domainFilter = null,
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

    // ── Visibility Configuration ────────────────────────────────

    /// <summary>
    /// Get the current entity visibility configuration (exposed flag + per-entity agent mappings).
    /// </summary>
    Task<EntityVisibilityConfig> GetVisibilityConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Set whether to load only HA-exposed entities.
    /// Triggers a full reload from Home Assistant when the value changes.
    /// </summary>
    Task SetUseExposedOnlyAsync(bool useExposedOnly, CancellationToken ct = default);

    /// <summary>
    /// Set agent visibility for one or more entities.
    /// <c>null</c> agent list = visible to all agents.
    /// Empty agent list = excluded from all agents.
    /// </summary>
    Task SetEntityAgentsAsync(Dictionary<string, List<string>?> updates, CancellationToken ct = default);

    /// <summary>
    /// Clear all per-entity agent filters, resetting every entity to visible-to-all.
    /// </summary>
    Task ClearAllAgentFiltersAsync(CancellationToken ct = default);

    /// <summary>
    /// Evict a cached embedding for a floor, area, or entity.
    /// Valid itemType values: <c>floor</c>, <c>area</c>, <c>entity</c>.
    /// </summary>
    Task<bool> EvictEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default);

    /// <summary>
    /// Regenerate a cached embedding for a floor, area, or entity.
    /// Valid itemType values: <c>floor</c>, <c>area</c>, <c>entity</c>.
    /// </summary>
    Task<bool> RegenerateEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default);

    /// <summary>
    /// Get current embedding generation progress across floors, areas, and entities.
    /// </summary>
    Task<EntityLocationEmbeddingProgress> GetEmbeddingProgressAsync(CancellationToken ct = default);
}
