using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Searches a <see cref="LocationSnapshot"/> of floors, areas and entities. Holds no state, so any
/// store that can produce a snapshot can reuse it.
/// </summary>
public sealed class LocationSearchService
{
    public static readonly HybridMatchOptions DefaultOptions = new()
    {
        EmbeddingWeight = 0.55,
        ScoreDropoffRatio = 0.9,
        Threshold = 0.2
    };

    private static readonly ActivitySource ActivitySource = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Counter<long> SearchCount = Meter.CreateCounter<long>("entity_location.search.count");
    private static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>("entity_location.search.duration", "ms");

    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly IHybridEntityMatcher _entityMatcher;
    private readonly ILogger<LocationSearchService> _logger;

    public LocationSearchService(
        IEmbeddingProviderResolver embeddingResolver,
        IHybridEntityMatcher entityMatcher,
        ILogger<LocationSearchService> logger)
    {
        _embeddingResolver = embeddingResolver;
        _entityMatcher = entityMatcher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        LocationSnapshot snap,
        string locationName,
        IReadOnlyList<string>? domainFilter,
        HybridMatchOptions hybridMatchOptions,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.query", locationName);
        var start = Stopwatch.GetTimestamp();
        SearchCount.Add(1);

        try
        {
            var matchedAreaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? matchType = null;

            var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);

            // 1. Hybrid search against areas (name + aliases scored by HybridEntityMatcher)
            if (embeddingService is not null)
            {
                var areaMatches = await _entityMatcher.FindMatchesAsync(
                    locationName, snap.Areas, embeddingService,
                    hybridMatchOptions, ct).ConfigureAwait(false);

                foreach (var match in areaMatches)
                {
                    matchedAreaIds.Add(match.Entity.AreaId);
                    matchType = "hybrid_area";
                }
            }

            // 2. Hybrid search against floors → expand to all areas on matched floors
            if (matchedAreaIds.Count == 0 && embeddingService is not null)
            {
                var floorMatches = await _entityMatcher.FindMatchesAsync(
                    locationName, snap.Floors, embeddingService,
                    hybridMatchOptions, ct).ConfigureAwait(false);

                foreach (var floorMatch in floorMatches)
                {
                    foreach (var area in snap.Areas)
                    {
                        if (area.FloorId == floorMatch.Entity.FloorId)
                            matchedAreaIds.Add(area.AreaId);
                    }
                    matchType = "hybrid_floor";
                }
            }

            activity?.SetTag("search.match_type", matchType ?? "none");
            activity?.SetTag("search.matched_areas", matchedAreaIds.Count);

            if (matchedAreaIds.Count == 0)
            {
                _logger.LogDebug("No location match found for '{LocationName}'", locationName);
                return ImmutableArray<HomeAssistantEntity>.Empty;
            }

            // Collect entities from matched areas, applying domain filter
            var result = snap.Entities
                .Where(e => e.AreaId is not null && matchedAreaIds.Contains(e.AreaId));

            if (domainFilter is { Count: > 0 })
            {
                var filterSet = domainFilter.ToHashSet(StringComparer.OrdinalIgnoreCase);
                result = result.Where(e => filterSet.Contains(e.Domain));
            }

            var matched = result.ToImmutableArray();

            _logger.LogDebug(
                "Location search '{LocationName}' matched {MatchType}: {AreaCount} areas, {EntityCount} entities",
                locationName, matchType, matchedAreaIds.Count, matched.Length);

            return matched;
        }
        finally
        {
            SearchDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public async Task<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>> SearchEntitiesAsync(
        LocationSnapshot snap,
        string query,
        IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        var candidates = (IReadOnlyList<HomeAssistantEntity>)snap.Entities;
        if (domainFilter is { Count: > 0 })
        {
            var filterSet = domainFilter.ToHashSet(StringComparer.OrdinalIgnoreCase);
            candidates = snap.Entities.Where(e => filterSet.Contains(e.Domain)).ToList();
        }

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
        {
            // Fallback: substring match on FriendlyName and EntityId when no embedding provider
            return candidates
                .Where(e =>
                    (e.FriendlyName is not null && e.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    || e.EntityId.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .Select(e => new EntityMatchResult<HomeAssistantEntity>
                {
                    Entity = e,
                    HybridScore = 1.0,
                    EmbeddingSimilarity = 0.0
                })
                .ToList();
        }

        return await _entityMatcher.FindMatchesAsync(
            query, candidates, embeddingService, options ?? DefaultOptions, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityMatchResult<AreaInfo>>> SearchAreasAsync(
        LocationSnapshot snap,
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
            return [];

        return await _entityMatcher.FindMatchesAsync(
            query, snap.Areas, embeddingService, options ?? DefaultOptions, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityMatchResult<FloorInfo>>> SearchFloorsAsync(
        LocationSnapshot snap,
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
            return [];

        return await _entityMatcher.FindMatchesAsync(
            query, snap.Floors, embeddingService, options ?? DefaultOptions, ct).ConfigureAwait(false);
    }

    public async Task<HierarchicalSearchResult> SearchHierarchyAsync(
        LocationSnapshot snap,
        string query,
        HybridMatchOptions? options,
        IReadOnlyList<string>? domainFilter,
        string? callerAgentId,
        CancellationToken ct = default)
    {
        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        var opts = options ?? DefaultOptions;

        IReadOnlyList<EntityMatchResult<FloorInfo>> floorMatches = [];
        IReadOnlyList<EntityMatchResult<AreaInfo>> areaMatches = [];
        IReadOnlyList<EntityMatchResult<HomeAssistantEntity>> entityMatches = [];

        // An empty allow-list hides the entity from every agent, with or without a domain filter.
        var filteredEntities = snap.Entities
            .Where(e => e.IncludeForAgent is null || e.IncludeForAgent.Count > 0)
            .Where(e => domainFilter is not { Count: > 0 }
                || domainFilter.Contains(e.Domain, StringComparer.OrdinalIgnoreCase))
            .ToList() as IReadOnlyList<HomeAssistantEntity>;
        if (callerAgentId is not null)
        {
            filteredEntities = filteredEntities
                .Where(entity => entity.IncludeForAgent is null
                    || entity.IncludeForAgent.Contains(callerAgentId))
                .ToList();
        }

        if (embeddingService is not null)
        {
            // Search all three levels in parallel
            var floorTask = _entityMatcher.FindMatchesAsync(
                query, snap.Floors, embeddingService, opts, ct);
            var areaTask = _entityMatcher.FindMatchesAsync(
                query, snap.Areas, embeddingService, opts, ct);
            var entityTask = _entityMatcher.FindMatchesAsync(
                query, filteredEntities, embeddingService, opts, ct);

            await Task.WhenAll(floorTask, areaTask, entityTask).ConfigureAwait(false);

            floorMatches = await floorTask.ConfigureAwait(false);
            areaMatches = await areaTask.ConfigureAwait(false);
            entityMatches = await entityTask.ConfigureAwait(false);
        }

        // Compare best embedding similarity across levels to decide resolution path
        var bestEntityHybrid = entityMatches.Count > 0
            ? entityMatches.Max(m => m.HybridScore)
            : (double?)null;

        var bestAreaHybrid = areaMatches.Count > 0
            ? areaMatches.Max(m => m.HybridScore)
            : (double?)null;
        var bestFloorHybrid = floorMatches.Count > 0
            ? floorMatches.Max(m => m.HybridScore)
            : (double?)null;

        var margin = opts.EmbeddingResolutionMargin;

        ResolutionStrategy strategy;
        string reason;
        List<HomeAssistantEntity> resolvedEntities;

        if (bestEntityHybrid is not null
            && (bestAreaHybrid is null || bestAreaHybrid - bestEntityHybrid < margin)
            && (bestFloorHybrid is null || bestFloorHybrid - bestEntityHybrid < margin))
        {
            // Entity embedding is clearly stronger — user means a specific device
            strategy = ResolutionStrategy.Entity;
            reason = $"Entity path: best entity score {bestEntityHybrid:F4}"
                   + $", area {bestAreaHybrid?.ToString("F4") ?? "none"}"
                   + $", floor {bestFloorHybrid?.ToString("F4") ?? "none"}"
                   + $" (margin {margin:F2})";
            resolvedEntities = entityMatches.Select(m => m.Entity).ToList();
        }
        else if (bestAreaHybrid is not null
            && (bestFloorHybrid is null || bestFloorHybrid - bestAreaHybrid < margin))
        {
            // Area is the best match — expand to all entities in matched areas
            strategy = ResolutionStrategy.Area;
            reason = $"Area path: best area score {bestAreaHybrid:F4}"
                   + $", entity {bestEntityHybrid?.ToString("F4") ?? "none"}"
                   + $", floor {bestFloorHybrid?.ToString("F4") ?? "none"}"
                   + $" (margin {margin:F2})";

            var resolvedAreaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var am in areaMatches)
                resolvedAreaIds.Add(am.Entity.AreaId);

            resolvedEntities = filteredEntities
                .Where(e => e.AreaId is not null && resolvedAreaIds.Contains(e.AreaId))
                .ToList();
        }
        else if (bestFloorHybrid is not null)
        {
            // Floor match — expand floors → areas → entities
            strategy = ResolutionStrategy.Floor;
            reason = $"Floor path: best floor score {bestFloorHybrid:F4}";

            var resolvedAreaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fm in floorMatches)
            {
                foreach (var area in snap.Areas)
                {
                    if (string.Equals(area.FloorId, fm.Entity.FloorId, StringComparison.OrdinalIgnoreCase))
                        resolvedAreaIds.Add(area.AreaId);
                }
            }

            resolvedEntities = filteredEntities
                .Where(e => e.AreaId is not null && resolvedAreaIds.Contains(e.AreaId))
                .ToList();
        }
        else
        {
            strategy = ResolutionStrategy.None;
            reason = "No matches at any level";
            resolvedEntities = [];
        }

        return new HierarchicalSearchResult
        {
            FloorMatches = floorMatches,
            AreaMatches = areaMatches,
            EntityMatches = entityMatches,
            ResolvedEntities = resolvedEntities,
            ResolutionStrategy = strategy,
            ResolutionReason = reason,
            BestEntityScore = bestEntityHybrid,
            BestAreaScore = bestAreaHybrid,
            BestFloorScore = bestFloorHybrid
        };
    }

    public static AreaInfo? GetAreaForEntity(LocationSnapshot snap, string entityId)
    {
        if (!snap.EntityById.TryGetValue(entityId, out var entity) || entity.AreaId is null)
            return null;

        snap.AreaById.TryGetValue(entity.AreaId, out var area);
        return area;
    }

    public static FloorInfo? GetFloorForArea(LocationSnapshot snap, string areaId)
    {
        if (!snap.AreaById.TryGetValue(areaId, out var area) || area.FloorId is null)
            return null;

        snap.FloorById.TryGetValue(area.FloorId, out var floor);
        return floor;
    }

    public static IReadOnlyList<HomeAssistantEntity> ExactMatchEntities(
        LocationSnapshot snap, string query, IReadOnlyList<string>? domainFilter = null)
    {
        if (snap.Entities.IsEmpty)
            return [];

        var trimmed = query.Trim();

        // 1. Exact entity_id match
        if (snap.EntityById.TryGetValue(trimmed, out var exactEntity))
        {
            if (domainFilter is null || domainFilter.Contains(exactEntity.Domain, StringComparer.OrdinalIgnoreCase))
                return [exactEntity];
            return [];
        }

        // 2. Exact area name match → all entities in that area filtered by domain
        var matchedArea = ExactMatchArea(snap, trimmed);
        if (matchedArea is not null)
        {
            var areaEntities = new List<HomeAssistantEntity>();
            foreach (var entity in snap.Entities)
            {
                if (string.Equals(entity.AreaId, matchedArea.AreaId, StringComparison.OrdinalIgnoreCase)
                    && (domainFilter is null || domainFilter.Contains(entity.Domain, StringComparer.OrdinalIgnoreCase)))
                {
                    areaEntities.Add(entity);
                }
            }

            if (areaEntities.Count > 0)
                return areaEntities;
        }

        // 3. Exact friendly name match
        var results = new List<HomeAssistantEntity>();
        foreach (var entity in snap.Entities)
        {
            if (string.Equals(entity.FriendlyName, trimmed, StringComparison.OrdinalIgnoreCase)
                && (domainFilter is null || domainFilter.Contains(entity.Domain, StringComparer.OrdinalIgnoreCase)))
            {
                results.Add(entity);
            }
        }

        return results;
    }

    public static AreaInfo? ExactMatchArea(LocationSnapshot snap, string query)
    {
        if (snap.Areas.IsEmpty)
            return null;

        var trimmed = query.Trim();

        // Display names only; area_id is an internal Home Assistant identifier.
        foreach (var a in snap.Areas)
        {
            if (string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }
}
