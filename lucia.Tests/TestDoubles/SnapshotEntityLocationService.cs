using System.Collections.Immutable;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// In-memory <see cref="IEntityLocationService"/> backed by the HA snapshot JSON.
/// Uses simple case-insensitive substring matching instead of embeddings,
/// making it suitable for eval harness and integration tests that don't need
/// a Redis cache or embedding provider.
/// </summary>
internal sealed class SnapshotEntityLocationService : IEntityLocationService
{
    private readonly List<AreaInfo> _areas = [];
    private readonly List<HomeAssistantEntity> _entities = [];
    private readonly Dictionary<string, string> _entityToArea = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastLoadedAt { get; private set; }

    /// <summary>
    /// Loads from the same snapshot format used by <see cref="FakeHomeAssistantClient"/>.
    /// </summary>
    public static SnapshotEntityLocationService FromSnapshotFile(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var svc = new SnapshotEntityLocationService();

        // Load areas
        if (doc.RootElement.TryGetProperty("areas", out var areasEl))
        {
            foreach (var el in areasEl.EnumerateArray())
            {
                var areaId = el.GetProperty("area_id").GetString() ?? "";
                var areaName = el.GetProperty("area_name").GetString() ?? areaId;
                var entityIds = new List<string>();

                if (el.TryGetProperty("entities", out var entitiesEl))
                {
                    foreach (var eid in entitiesEl.EnumerateArray())
                    {
                        var id = eid.GetString();
                        if (id is not null)
                        {
                            entityIds.Add(id);
                            svc._entityToArea[id] = areaId;
                        }
                    }
                }

                svc._areas.Add(new AreaInfo
                {
                    AreaId = areaId,
                    Name = areaName,
                    EntityIds = entityIds
                });
            }
        }

        // Load entities from snapshot sections
        LoadEntities(doc, "lights", svc);
        LoadEntities(doc, "media_players", svc);

        svc.LastLoadedAt = DateTimeOffset.UtcNow;
        return svc;
    }

    private static void LoadEntities(JsonDocument doc, string section, SnapshotEntityLocationService svc)
    {
        if (!doc.RootElement.TryGetProperty(section, out var el)) return;

        foreach (var item in el.EnumerateArray())
        {
            var entityId = item.GetProperty("entity_id").GetString() ?? "";
            var friendlyName = entityId;

            if (item.TryGetProperty("attributes", out var attrs) &&
                attrs.TryGetProperty("friendly_name", out var fn))
            {
                friendlyName = fn.GetString() ?? entityId;
            }

            svc._entityToArea.TryGetValue(entityId, out var areaId);

            svc._entities.Add(new HomeAssistantEntity
            {
                EntityId = entityId,
                FriendlyName = friendlyName,
                AreaId = areaId
            });
        }
    }

    // ─── IEntityLocationService Implementation ────────────────────

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task InvalidateAndReloadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<FloorInfo>> GetFloorsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FloorInfo>>([]);

    public Task<IReadOnlyList<AreaInfo>> GetAreasAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AreaInfo>>(_areas);

    public Task<IReadOnlyList<HomeAssistantEntity>> GetEntitiesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(_entities);

    public Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName, CancellationToken ct = default)
        => FindEntitiesByLocationAsync(locationName, null, ct);

    public Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName, IReadOnlyList<string>? domainFilter, CancellationToken ct = default)
        => FindEntitiesByLocationAsync(locationName, domainFilter, null, ct);

    public Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName, IReadOnlyList<string>? domainFilter,
        HybridMatchOptions? hybridMatchOptions, CancellationToken ct = default)
    {
        var result = SearchHierarchySync(locationName, domainFilter);
        return Task.FromResult<IReadOnlyList<HomeAssistantEntity>>(result.ResolvedEntities.ToList());
    }

    public Task<HierarchicalSearchResult> SearchHierarchyAsync(
        string query, HybridMatchOptions? options = null,
        IReadOnlyList<string>? domainFilter = null, CancellationToken ct = default)
    {
        return Task.FromResult(SearchHierarchySync(query, domainFilter));
    }

    public Task<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>> SearchEntitiesAsync(
        string query, IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null, CancellationToken ct = default)
    {
        var matches = _entities
            .Where(e => MatchesQuery(e.FriendlyName, query) || MatchesQuery(e.EntityId, query))
            .Where(e => domainFilter is null || domainFilter.Count == 0 || domainFilter.Contains(e.Domain))
            .Select(e => new EntityMatchResult<HomeAssistantEntity>
            {
                Entity = e, HybridScore = 0.85, EmbeddingSimilarity = 0.85
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>>(matches);
    }

    public Task<IReadOnlyList<EntityMatchResult<AreaInfo>>> SearchAreasAsync(
        string query, HybridMatchOptions? options = null, CancellationToken ct = default)
    {
        var matches = _areas
            .Where(a => MatchesQuery(a.Name, query))
            .Select(a => new EntityMatchResult<AreaInfo>
            {
                Entity = a, HybridScore = 0.9, EmbeddingSimilarity = 0.9
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<EntityMatchResult<AreaInfo>>>(matches);
    }

    public Task<IReadOnlyList<EntityMatchResult<FloorInfo>>> SearchFloorsAsync(
        string query, HybridMatchOptions? options = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EntityMatchResult<FloorInfo>>>([]);

    public AreaInfo? GetAreaForEntity(string entityId)
    {
        _entityToArea.TryGetValue(entityId, out var areaId);
        return areaId is not null ? _areas.FirstOrDefault(a => a.AreaId == areaId) : null;
    }

    public FloorInfo? GetFloorForArea(string areaId) => null;

    // ── Synchronous, cache-only fast-path lookups ─────────────────

    public LocationSnapshot GetSnapshot()
    {
        var areas = _areas.ToImmutableArray();
        var entities = _entities.ToImmutableArray();

        return new LocationSnapshot(
            ImmutableArray<FloorInfo>.Empty,
            areas,
            entities,
            ImmutableDictionary<string, FloorInfo>.Empty,
            areas.ToImmutableDictionary(a => a.AreaId, StringComparer.OrdinalIgnoreCase),
            entities.ToImmutableDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase));
    }

    public bool IsCacheReady => LastLoadedAt is not null;

    public IReadOnlyList<HomeAssistantEntity> ExactMatchEntities(string query, IReadOnlyList<string>? domainFilter = null)
    {
        if (!IsCacheReady || _entities.Count == 0)
            return [];

        var trimmed = query.Trim();

        var exactById = _entities.FirstOrDefault(e =>
            string.Equals(e.EntityId, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exactById is not null)
        {
            if (domainFilter is null || domainFilter.Contains(exactById.Domain, StringComparer.OrdinalIgnoreCase))
                return [exactById];
            return [];
        }

        var matchedArea = ExactMatchArea(trimmed);
        if (matchedArea is not null)
        {
            var areaEntities = _entities
                .Where(e => string.Equals(e.AreaId, matchedArea.AreaId, StringComparison.OrdinalIgnoreCase)
                    && (domainFilter is null || domainFilter.Contains(e.Domain, StringComparer.OrdinalIgnoreCase)))
                .ToList();
            if (areaEntities.Count > 0)
                return areaEntities;
        }

        return _entities
            .Where(e => string.Equals(e.FriendlyName, trimmed, StringComparison.OrdinalIgnoreCase)
                && (domainFilter is null || domainFilter.Contains(e.Domain, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    public AreaInfo? ExactMatchArea(string query)
    {
        if (!IsCacheReady || _areas.Count == 0)
            return null;

        var trimmed = query.Trim();
        return _areas.FirstOrDefault(a =>
            string.Equals(a.AreaId, trimmed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    // ── Visibility stubs (not needed for eval) ────────────────────

    public Task<EntityVisibilityConfig> GetVisibilityConfigAsync(CancellationToken ct = default)
        => Task.FromResult(new EntityVisibilityConfig());

    public Task SetUseExposedOnlyAsync(bool useExposedOnly, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetEntityAgentsAsync(Dictionary<string, List<string>?> updates, CancellationToken ct = default) => Task.CompletedTask;
    public Task ClearAllAgentFiltersAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> EvictEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> RegenerateEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> RemoveEntityAsync(string entityId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<EntityLocationEmbeddingProgress> GetEmbeddingProgressAsync(CancellationToken ct = default)
        => Task.FromResult(new EntityLocationEmbeddingProgress
        {
            FloorTotalCount = 0,
            FloorGeneratedCount = 0,
            AreaTotalCount = _areas.Count,
            AreaGeneratedCount = _areas.Count,
            EntityTotalCount = _entities.Count,
            EntityGeneratedCount = _entities.Count,
            EntityMissingCount = 0,
            IsGenerationRunning = false
        });

    // ── Dynamic Registration ─────────────────────────────────────

    /// <summary>
    /// Registers a dynamically created entity so it becomes discoverable by
    /// <see cref="SearchHierarchyAsync"/>, <see cref="SearchEntitiesAsync"/>,
    /// and other lookup methods. Used by eval scenarios to inject entities
    /// that are not present in the static HA snapshot file.
    /// </summary>
    public void RegisterEntity(string entityId, string? friendlyName = null, string? areaId = null)
    {
        // Avoid duplicates
        if (_entities.Any(e => string.Equals(e.EntityId, entityId, StringComparison.OrdinalIgnoreCase)))
            return;

        _entities.Add(new HomeAssistantEntity
        {
            EntityId = entityId,
            FriendlyName = friendlyName ?? entityId,
            AreaId = areaId
        });

        if (areaId is not null)
            _entityToArea[entityId] = areaId;

        // Ensure LastLoadedAt is set so IsCacheReady returns true
        LastLoadedAt ??= DateTimeOffset.UtcNow;
    }

    // ── Private helpers ───────────────────────────────────────────

    private HierarchicalSearchResult SearchHierarchySync(string query, IReadOnlyList<string>? domainFilter)
    {
        // Match areas by name
        var matchedAreas = _areas
            .Where(a => MatchesQuery(a.Name, query))
            .ToList();

        // Get entities from matched areas
        var areaEntityIds = matchedAreas
            .SelectMany(a => a.EntityIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var areaEntities = _entities
            .Where(e => areaEntityIds.Contains(e.EntityId))
            .Where(e => domainFilter is null || domainFilter.Count == 0 || domainFilter.Contains(e.Domain))
            .ToList();

        // Direct entity matches (by friendly name or entity ID)
        var directMatches = _entities
            .Where(e => MatchesQuery(e.FriendlyName, query) || MatchesQuery(e.EntityId, query))
            .Where(e => domainFilter is null || domainFilter.Count == 0 || domainFilter.Contains(e.Domain))
            .ToList();

        // Combine, deduplicate
        var resolved = areaEntities
            .Concat(directMatches)
            .DistinctBy(e => e.EntityId)
            .ToList();

        var strategy = matchedAreas.Count > 0
            ? ResolutionStrategy.Area
            : directMatches.Count > 0
                ? ResolutionStrategy.Entity
                : ResolutionStrategy.None;

        return new HierarchicalSearchResult
        {
            FloorMatches = [],
            AreaMatches = matchedAreas.Select(a => new EntityMatchResult<AreaInfo>
            {
                Entity = a, HybridScore = 0.9, EmbeddingSimilarity = 0.9
            }).ToList(),
            EntityMatches = directMatches.Select(e => new EntityMatchResult<HomeAssistantEntity>
            {
                Entity = e, HybridScore = 0.85, EmbeddingSimilarity = 0.85
            }).ToList(),
            ResolvedEntities = resolved,
            ResolutionStrategy = strategy,
            ResolutionReason = $"Snapshot match: {resolved.Count} entities for '{query}'",
            BestEntityScore = directMatches.Count > 0 ? 0.85 : null,
            BestAreaScore = matchedAreas.Count > 0 ? 0.9 : null,
            BestFloorScore = null
        };
    }

    /// <summary>
    /// Bidirectional substring match: either the text contains the query,
    /// or the query contains the text. This allows area names like "Living Room"
    /// to match queries like "living room lights" where the query embeds the area name.
    /// </summary>
    private static bool MatchesQuery(string? text, string query) =>
        text is not null &&
        (text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
         query.Contains(text, StringComparison.OrdinalIgnoreCase));
}
