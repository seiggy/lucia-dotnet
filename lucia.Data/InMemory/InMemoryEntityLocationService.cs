using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Data.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IEntityLocationService"/> that loads from Home Assistant
/// and stores all location data in-process without any Redis persistence.
/// Designed for lightweight/mono-container deployments where cross-process cache sharing is not needed.
/// </summary>
public sealed class InMemoryEntityLocationService : IEntityLocationService
{
    private const int EmbeddingBatchSize = 25;
    private const int EmbeddingBatchMaxAttempts = 3;
    private static readonly TimeSpan EmbeddingBatchDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EmbeddingRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EmptyCacheRetryInterval = TimeSpan.FromSeconds(30);

    private static readonly HybridMatchOptions DefaultLocationOptions = new()
    {
        EmbeddingWeight = 0.55,
        ScoreDropoffRatio = 0.9,
        Threshold = 0.2
    };

    private readonly IHomeAssistantClient _haClient;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly IHybridEntityMatcher _entityMatcher;
    private readonly ILogger<InMemoryEntityLocationService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly object _embeddingJobGate = new();
    private CancellationTokenSource? _embeddingJobCts;
    private long _embeddingJobCounter;
    private long _activeEmbeddingJobId;
    private int _isEmbeddingGenerationRunning;
    private long _lastEmptyCacheRetryTicks;

    private volatile LocationSnapshot _snapshot = LocationSnapshot.Empty;
    private volatile EntityVisibilityConfig _visibilityConfig = new();
    private long _lastLoadedAtTicks;

    public DateTimeOffset? LastLoadedAt
    {
        get
        {
            var ticks = Volatile.Read(ref _lastLoadedAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public InMemoryEntityLocationService(
        IHomeAssistantClient haClient,
        IEmbeddingProviderResolver embeddingResolver,
        IHybridEntityMatcher entityMatcher,
        ILogger<InMemoryEntityLocationService> logger)
    {
        _haClient = haClient;
        _embeddingResolver = embeddingResolver;
        _entityMatcher = entityMatcher;
        _logger = logger;
    }

    // ── Initialization ──────────────────────────────────────────────

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!_snapshot.Entities.IsEmpty)
        {
            _logger.LogDebug("In-memory entity location cache already initialized ({Count} entities), skipping",
                _snapshot.Entities.Length);
            return;
        }

        await LoadFromHomeAssistantAsync(ct).ConfigureAwait(false);
    }

    public async Task InvalidateAndReloadAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Invalidating in-memory entity location cache, reloading from Home Assistant");
        await LoadFromHomeAssistantAsync(ct).ConfigureAwait(false);
    }

    // ── Queries ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FloorInfo>> GetFloorsAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        return _snapshot.Floors;
    }

    public async Task<IReadOnlyList<AreaInfo>> GetAreasAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        return _snapshot.Areas;
    }

    public async Task<IReadOnlyList<HomeAssistantEntity>> GetEntitiesAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        return _snapshot.Entities;
    }

    public Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName, CancellationToken ct = default)
        => FindEntitiesByLocationAsync(locationName, domainFilter: null, DefaultLocationOptions, ct);

    public Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName,
        IReadOnlyList<string>? domainFilter,
        CancellationToken ct = default)
        => FindEntitiesByLocationAsync(locationName, domainFilter, DefaultLocationOptions, ct);

    public async Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName,
        IReadOnlyList<string>? domainFilter,
        HybridMatchOptions hybridMatchOptions,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        var snap = _snapshot;
        var matchedAreaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);

        // Hybrid search against areas
        if (embeddingService is not null)
        {
            var areaMatches = await _entityMatcher.FindMatchesAsync(
                locationName, snap.Areas, embeddingService,
                hybridMatchOptions, ct).ConfigureAwait(false);

            foreach (var match in areaMatches)
                matchedAreaIds.Add(match.Entity.AreaId);
        }

        // Fallback: search against floors and expand to their areas
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
            }
        }

        if (matchedAreaIds.Count == 0)
            return ImmutableArray<HomeAssistantEntity>.Empty;

        var result = snap.Entities
            .Where(e => e.AreaId is not null && matchedAreaIds.Contains(e.AreaId));

        if (domainFilter is { Count: > 0 })
        {
            var filterSet = domainFilter.ToHashSet(StringComparer.OrdinalIgnoreCase);
            result = result.Where(e => filterSet.Contains(e.Domain));
        }

        return result.ToImmutableArray();
    }

    public async Task<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>> SearchEntitiesAsync(
        string query,
        IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
            return [];

        var candidates = (IReadOnlyList<HomeAssistantEntity>)_snapshot.Entities;
        if (domainFilter is { Count: > 0 })
        {
            var filterSet = domainFilter.ToHashSet(StringComparer.OrdinalIgnoreCase);
            candidates = _snapshot.Entities.Where(e => filterSet.Contains(e.Domain)).ToList();
        }

        return await _entityMatcher.FindMatchesAsync(
            query, candidates, embeddingService, options ?? DefaultLocationOptions, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityMatchResult<AreaInfo>>> SearchAreasAsync(
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
            return [];

        return await _entityMatcher.FindMatchesAsync(
            query, _snapshot.Areas, embeddingService, options ?? DefaultLocationOptions, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityMatchResult<FloorInfo>>> SearchFloorsAsync(
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
            return [];

        return await _entityMatcher.FindMatchesAsync(
            query, _snapshot.Floors, embeddingService, options ?? DefaultLocationOptions, ct).ConfigureAwait(false);
    }

    public async Task<HierarchicalSearchResult> SearchHierarchyAsync(
        string query,
        HybridMatchOptions? options = null,
        IReadOnlyList<string>? domainFilter = null,
        CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        var opts = options ?? DefaultLocationOptions;
        var snap = _snapshot;

        IReadOnlyList<EntityMatchResult<FloorInfo>> floorMatches = [];
        IReadOnlyList<EntityMatchResult<AreaInfo>> areaMatches = [];
        IReadOnlyList<EntityMatchResult<HomeAssistantEntity>> entityMatches = [];

        var filteredEntities = domainFilter is { Count: > 0 }
            ? snap.Entities.Where(e =>
                    domainFilter.Contains(e.Domain, StringComparer.OrdinalIgnoreCase) &&
                    (e.IncludeForAgent == null || e.IncludeForAgent.Count > 0))
                .ToList()
            : (IReadOnlyList<HomeAssistantEntity>)snap.Entities;

        if (embeddingService is not null)
        {
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

        var bestEntityHybrid = entityMatches.Count > 0 ? entityMatches.Max(m => m.HybridScore) : (double?)null;
        var bestAreaHybrid = areaMatches.Count > 0 ? areaMatches.Max(m => m.HybridScore) : (double?)null;
        var bestFloorHybrid = floorMatches.Count > 0 ? floorMatches.Max(m => m.HybridScore) : (double?)null;

        var margin = opts.EmbeddingResolutionMargin;

        ResolutionStrategy strategy;
        string reason;
        List<HomeAssistantEntity> resolvedEntities;

        if (bestEntityHybrid is not null
            && (bestAreaHybrid is null || bestAreaHybrid - bestEntityHybrid < margin)
            && (bestFloorHybrid is null || bestFloorHybrid - bestEntityHybrid < margin))
        {
            strategy = ResolutionStrategy.Entity;
            reason = $"Entity path: best entity score {bestEntityHybrid:F4}";
            resolvedEntities = entityMatches.Select(m => m.Entity).ToList();
        }
        else if (bestAreaHybrid is not null
            && (bestFloorHybrid is null || bestFloorHybrid - bestAreaHybrid < margin))
        {
            strategy = ResolutionStrategy.Area;
            reason = $"Area path: best area score {bestAreaHybrid:F4}";

            var resolvedAreaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var am in areaMatches)
                resolvedAreaIds.Add(am.Entity.AreaId);

            resolvedEntities = filteredEntities
                .Where(e => e.AreaId is not null && resolvedAreaIds.Contains(e.AreaId))
                .ToList();
        }
        else if (bestFloorHybrid is not null)
        {
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

    // ── Synchronous lookups ─────────────────────────────────────────

    public AreaInfo? GetAreaForEntity(string entityId)
    {
        var snap = _snapshot;
        if (!snap.EntityById.TryGetValue(entityId, out var entity) || entity.AreaId is null)
            return null;

        snap.AreaById.TryGetValue(entity.AreaId, out var area);
        return area;
    }

    public FloorInfo? GetFloorForArea(string areaId)
    {
        var snap = _snapshot;
        if (!snap.AreaById.TryGetValue(areaId, out var area) || area.FloorId is null)
            return null;

        snap.FloorById.TryGetValue(area.FloorId, out var floor);
        return floor;
    }

    // ── Synchronous, cache-only fast-path lookups ───────────────────

    /// <inheritdoc />
    public bool IsCacheReady => Volatile.Read(ref _lastLoadedAtTicks) != 0;

    /// <inheritdoc />
    public IReadOnlyList<HomeAssistantEntity> ExactMatchEntities(string query, IReadOnlyList<string>? domainFilter = null)
    {
        var snap = _snapshot;
        if (snap.Entities.IsEmpty)
            return [];

        var trimmed = query.Trim();

        if (snap.EntityById.TryGetValue(trimmed, out var exactEntity))
        {
            if (domainFilter is null || domainFilter.Contains(exactEntity.Domain, StringComparer.OrdinalIgnoreCase))
                return [exactEntity];
            return [];
        }

        var matchedArea = ExactMatchArea(trimmed);
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

    /// <inheritdoc />
    public AreaInfo? ExactMatchArea(string query)
    {
        var snap = _snapshot;
        if (snap.Areas.IsEmpty)
            return null;

        var trimmed = query.Trim();

        if (snap.AreaById.TryGetValue(trimmed, out var area))
            return area;

        foreach (var a in snap.Areas)
        {
            if (string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    // ── Visibility ──────────────────────────────────────────────────

    public Task<EntityVisibilityConfig> GetVisibilityConfigAsync(CancellationToken ct = default)
        => Task.FromResult(_visibilityConfig);

    public async Task SetUseExposedOnlyAsync(bool useExposedOnly, CancellationToken ct = default)
    {
        if (_visibilityConfig.UseExposedEntitiesOnly == useExposedOnly)
            return;

        _visibilityConfig.UseExposedEntitiesOnly = useExposedOnly;
        await InvalidateAndReloadAsync(ct).ConfigureAwait(false);
    }

    public Task SetEntityAgentsAsync(Dictionary<string, List<string>?> updates, CancellationToken ct = default)
    {
        foreach (var (entityId, agents) in updates)
        {
            if (agents is null)
                _visibilityConfig.EntityAgentMap.Remove(entityId);
            else
                _visibilityConfig.EntityAgentMap[entityId] = agents;
        }

        ApplyVisibilityToEntities();
        return Task.CompletedTask;
    }

    public Task ClearAllAgentFiltersAsync(CancellationToken ct = default)
    {
        _visibilityConfig.EntityAgentMap.Clear();
        ApplyVisibilityToEntities();
        return Task.CompletedTask;
    }

    // ── Embedding management ────────────────────────────────────────

    public Task<bool> EvictEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default)
    {
        if (!TryResolveEmbeddingTarget(itemType, itemId, out _, out var setEmbedding))
            return Task.FromResult(false);

        setEmbedding(null);
        return Task.FromResult(true);
    }

    public async Task<bool> RegenerateEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default)
    {
        if (!TryResolveEmbeddingTarget(itemType, itemId, out var matchableName, out var setEmbedding))
            return false;

        if (string.IsNullOrWhiteSpace(matchableName))
            return false;

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
            return false;

        try
        {
            var embedding = await embeddingService.GenerateAsync(matchableName, cancellationToken: ct).ConfigureAwait(false);
            setEmbedding(CloneEmbedding(embedding));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to regenerate embedding for {ItemType} '{ItemId}'", itemType, itemId);
            return false;
        }
    }

    public Task<bool> RemoveEntityAsync(string entityId, CancellationToken ct = default)
    {
        var snap = _snapshot;
        if (!snap.EntityById.TryGetValue(entityId, out var entity))
            return Task.FromResult(false);

        var newEntities = snap.Entities.Remove(entity);
        SwapData(snap.Floors, snap.Areas, newEntities);

        _logger.LogInformation("Removed entity '{EntityId}' from in-memory location cache", entityId);
        return Task.FromResult(true);
    }

    public Task<EntityLocationEmbeddingProgress> GetEmbeddingProgressAsync(CancellationToken ct = default)
    {
        var snap = _snapshot;
        return Task.FromResult(new EntityLocationEmbeddingProgress
        {
            FloorTotalCount = snap.Floors.Length,
            FloorGeneratedCount = snap.Floors.Count(f => f.NameEmbedding is not null),
            AreaTotalCount = snap.Areas.Length,
            AreaGeneratedCount = snap.Areas.Count(a => a.NameEmbedding is not null),
            EntityTotalCount = snap.Entities.Length,
            EntityGeneratedCount = snap.Entities.Count(e => e.NameEmbedding is not null),
            EntityMissingCount = Math.Max(snap.Entities.Length - snap.Entities.Count(e => e.NameEmbedding is not null), 0),
            IsGenerationRunning = Volatile.Read(ref _isEmbeddingGenerationRunning) == 1,
            LastLoadedAt = LastLoadedAt
        });
    }

    // ── Private: Loading ────────────────────────────────────────────

    private async Task LoadFromHomeAssistantAsync(CancellationToken ct)
    {
        if (!await _loadLock.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            await _loadLock.WaitAsync(ct).ConfigureAwait(false);
            _loadLock.Release();
            return;
        }

        try
        {
            _logger.LogInformation("Loading entity location data from Home Assistant config registries...");

            var floorTask = _haClient.GetFloorRegistryAsync(ct);
            var areaTask = _haClient.GetAreaRegistryAsync(ct);
            var entityTask = _haClient.GetEntityRegistryAsync(ct);

            await Task.WhenAll(floorTask, areaTask, entityTask).ConfigureAwait(false);

            var floorEntries = await floorTask.ConfigureAwait(false);
            var areaEntries = await areaTask.ConfigureAwait(false);
            var entityEntries = await entityTask.ConfigureAwait(false);

            if (_visibilityConfig.UseExposedEntitiesOnly)
            {
                try
                {
                    var exposed = await _haClient.GetExposedEntitiesAsync(ct).ConfigureAwait(false);
                    var exposedIds = new HashSet<string>(
                        exposed.Where(kv => kv.Value.IsExposedToAny).Select(kv => kv.Key),
                        StringComparer.OrdinalIgnoreCase);

                    var before = entityEntries.Length;
                    entityEntries = entityEntries.Where(e => exposedIds.Contains(e.EntityId)).ToArray();
                    _logger.LogInformation("Filtered to exposed entities only: {Before} \u2192 {After}",
                        before, entityEntries.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch exposed entity list \u2014 loading all entities instead");
                }
            }

            var areaEntityMap = await LoadAreaEntityMapAsync(ct).ConfigureAwait(false);

            var floors = BuildFloors(floorEntries);
            var areas = BuildAreas(areaEntries, areaEntityMap);
            var entities = BuildEntities(entityEntries, areaEntityMap);

            GeneratePhoneticData(floors, areas, entities);
            SwapData(floors, areas, entities);
            ApplyVisibilityToEntities();

            _logger.LogInformation(
                "Loaded location data (in-memory): {FloorCount} floors, {AreaCount} areas, {EntityCount} entities",
                floors.Length, areas.Length, entities.Length);

            StartEmbeddingGenerationJob(floors, areas, entities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load entity location data from Home Assistant");
            throw;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (!_snapshot.Entities.IsEmpty)
            return;

        var lastRetry = Volatile.Read(ref _lastEmptyCacheRetryTicks);
        if (lastRetry != 0 && Stopwatch.GetElapsedTime(lastRetry) < EmptyCacheRetryInterval)
            return;

        Volatile.Write(ref _lastEmptyCacheRetryTicks, Stopwatch.GetTimestamp());

        try
        {
            _logger.LogInformation("In-memory entity location cache is empty, attempting to load from Home Assistant...");
            await InitializeAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-reload of empty entity location cache failed \u2014 will retry in {Interval}s",
                EmptyCacheRetryInterval.TotalSeconds);
        }
    }

    private async Task<Dictionary<string, List<string>>> LoadAreaEntityMapAsync(CancellationToken ct)
    {
        const string template =
            "[{% for id in areas() %}{% if not loop.first %}, {% endif %}" +
            "{\"area_id\":\"{{ id }}\",\"area_name\":\"{{ area_name(id) }}\",\"entities\":" +
            "[{% for e in area_entities(id) %}{% if not loop.first %}, {% endif %}\"{{ e }}\"{% endfor %}]}" +
            "{% endfor %}]";

        try
        {
            var result = await _haClient.RunTemplateAsync<List<JinjaAreaResult>>(template, ct).ConfigureAwait(false);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in result)
                map[entry.AreaId] = entry.Entities;
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load area\u2192entity map via Jinja, falling back to registry area_id only");
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // ── Private: Build domain models ────────────────────────────────

    private static ImmutableArray<FloorInfo> BuildFloors(FloorRegistryEntry[] entries) =>
    [
        ..entries.Select(f => new FloorInfo
        {
            FloorId = f.FloorId,
            Aliases = EntityMatchNameFormatter.SanitizeAliases(f.Aliases).AsReadOnly(),
            Name = EntityMatchNameFormatter.ResolveName(f.Name, f.Aliases, f.FloorId),
            Level = f.Level,
            Icon = f.Icon
        })
    ];

    private static ImmutableArray<AreaInfo> BuildAreas(
        AreaRegistryEntry[] entries,
        Dictionary<string, List<string>> areaEntityMap) =>
    [
        ..entries.Select(a => new AreaInfo
        {
            AreaId = a.AreaId,
            Name = EntityMatchNameFormatter.ResolveName(a.Name, a.Aliases, a.AreaId),
            FloorId = a.FloorId,
            Aliases = EntityMatchNameFormatter.SanitizeAliases(a.Aliases).AsReadOnly(),
            EntityIds = areaEntityMap.TryGetValue(a.AreaId, out var entityIds)
                ? entityIds.AsReadOnly()
                : [],
            Icon = a.Icon,
            Labels = a.Labels.AsReadOnly()
        })
    ];

    private static ImmutableArray<HomeAssistantEntity> BuildEntities(
        EntityRegistryEntry[] entries,
        Dictionary<string, List<string>> areaEntityMap)
    {
        var entityToArea = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (areaId, entityIds) in areaEntityMap)
        {
            foreach (var entityId in entityIds)
                entityToArea.TryAdd(entityId, areaId);
        }

        return
        [
            ..entries
                .Where(e => e.DisabledBy is null)
                .Select(e => new HomeAssistantEntity
                {
                    EntityId = e.EntityId,
                    FriendlyName = EntityMatchNameFormatter.ResolveName(
                        e.Name ?? e.OriginalName,
                        e.Aliases,
                        e.EntityId,
                        stripDomainFromId: true),
                    Aliases = EntityMatchNameFormatter.SanitizeAliases(e.Aliases).AsReadOnly(),
                    AreaId = entityToArea.TryGetValue(e.EntityId, out var jinjaArea)
                        ? jinjaArea
                        : e.AreaId,
                    Platform = e.Platform,
                    SupportedFeatures = (SupportedFeaturesFlags)e.SupportedFeatures
                })
        ];
    }

    private static void GeneratePhoneticData(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities)
    {
        foreach (var floor in floors)
        {
            floor.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(floor.Name);
            floor.AliasPhoneticKeys = BuildAliasPhoneticKeys(floor.Aliases);
        }

        foreach (var area in areas)
        {
            area.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(area.Name);
            area.AliasPhoneticKeys = BuildAliasPhoneticKeys(area.Aliases);
        }

        foreach (var entity in entities)
        {
            entity.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(entity.FriendlyName);
            entity.AliasPhoneticKeys = BuildAliasPhoneticKeys(entity.Aliases);
        }
    }

    private static IReadOnlyList<string[]> BuildAliasPhoneticKeys(IReadOnlyList<string> aliases)
    {
        if (aliases.Count == 0)
            return [];

        var result = new string[aliases.Count][];
        for (var i = 0; i < aliases.Count; i++)
            result[i] = StringSimilarity.BuildPhoneticKeys(aliases[i]);
        return result;
    }

    // ── Private: Data swap ──────────────────────────────────────────

    private void SwapData(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities)
    {
        var floorById = floors.ToImmutableDictionary(f => f.FloorId, StringComparer.OrdinalIgnoreCase);
        var areaById = areas.ToImmutableDictionary(a => a.AreaId, StringComparer.OrdinalIgnoreCase);
        var entityById = entities.ToImmutableDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase);

        _snapshot = new LocationSnapshot(floors, areas, entities, floorById, areaById, entityById);
        Volatile.Write(ref _lastLoadedAtTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    // ── Private: Visibility ─────────────────────────────────────────

    private void ApplyVisibilityToEntities()
    {
        var config = _visibilityConfig;
        foreach (var entity in _snapshot.Entities)
        {
            entity.IncludeForAgent = config.EntityAgentMap.TryGetValue(entity.EntityId, out var agents)
                ? new HashSet<string>(agents, StringComparer.OrdinalIgnoreCase)
                : null;
        }
    }

    // ── Private: Embedding generation ───────────────────────────────

    private void StartEmbeddingGenerationJob(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities)
    {
        lock (_embeddingJobGate)
        {
            _embeddingJobCts?.Cancel();
            _embeddingJobCts?.Dispose();

            var cts = new CancellationTokenSource();
            _embeddingJobCts = cts;
            var jobId = Interlocked.Increment(ref _embeddingJobCounter);
            Volatile.Write(ref _activeEmbeddingJobId, jobId);
            Volatile.Write(ref _isEmbeddingGenerationRunning, 1);
            _ = Task.Run(
                () => RunEmbeddingGenerationJobAsync(floors, areas, entities, jobId, cts.Token),
                cts.Token);
        }
    }

    private async Task RunEmbeddingGenerationJobAsync(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities,
        long jobId,
        CancellationToken ct)
    {
        try
        {
            var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
            if (embeddingService is null)
            {
                _logger.LogWarning("No embedding provider available \u2014 embedding-based search will be disabled");
                return;
            }

            var namesToEmbed = new Dictionary<string, List<Action<Embedding<float>>>>(StringComparer.OrdinalIgnoreCase);

            void Register(string name, Action<Embedding<float>> setter, bool hasEmbedding)
            {
                if (hasEmbedding || string.IsNullOrWhiteSpace(name))
                    return;

                if (!namesToEmbed.TryGetValue(name, out var setters))
                {
                    setters = [];
                    namesToEmbed[name] = setters;
                }

                setters.Add(setter);
            }

            foreach (var floor in floors)
                Register(floor.Name, e => floor.NameEmbedding = e, floor.NameEmbedding is not null);
            foreach (var area in areas)
                Register(area.Name, e => area.NameEmbedding = e, area.NameEmbedding is not null);
            foreach (var entity in entities)
                Register(entity.FriendlyName, e => entity.NameEmbedding = e, entity.NameEmbedding is not null);

            if (namesToEmbed.Count == 0)
            {
                _logger.LogDebug("Embedding cache already complete for current location snapshot");
                return;
            }

            var names = namesToEmbed.Keys.ToList();
            var generated = 0;

            for (var offset = 0; offset < names.Count; offset += EmbeddingBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batchNames = names.Skip(offset).Take(EmbeddingBatchSize).ToArray();
                var batchEmbeddings = await TryGenerateBatchWithRetryAsync(embeddingService, batchNames, ct).ConfigureAwait(false);
                if (batchEmbeddings is null)
                    continue;

                var count = Math.Min(batchNames.Length, batchEmbeddings.Count);
                for (var i = 0; i < count; i++)
                {
                    if (!namesToEmbed.TryGetValue(batchNames[i], out var setters))
                        continue;

                    foreach (var setter in setters)
                        setter(CloneEmbedding(batchEmbeddings[i]));

                    generated++;
                }

                if (offset + EmbeddingBatchSize < names.Count)
                    await Task.Delay(EmbeddingBatchDelay, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Generated {GeneratedCount} embeddings for in-memory location snapshot (requested {RequestedCount})",
                generated, namesToEmbed.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Embedding generation job cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background embedding generation failed");
        }
        finally
        {
            if (Volatile.Read(ref _activeEmbeddingJobId) == jobId)
                Volatile.Write(ref _isEmbeddingGenerationRunning, 0);
        }
    }

    private async Task<GeneratedEmbeddings<Embedding<float>>?> TryGenerateBatchWithRetryAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddingService,
        string[] batchNames,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= EmbeddingBatchMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await embeddingService.GenerateAsync(batchNames, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < EmbeddingBatchMaxAttempts)
            {
                _logger.LogWarning(ex, "Embedding batch failed on attempt {Attempt}/{MaxAttempts}; retrying",
                    attempt, EmbeddingBatchMaxAttempts);
                await Task.Delay(EmbeddingRetryDelay, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Embedding batch failed after {MaxAttempts} attempts", EmbeddingBatchMaxAttempts);
                return null;
            }
        }

        return null;
    }

    // ── Private: Helpers ────────────────────────────────────────────

    private bool TryResolveEmbeddingTarget(
        string itemType,
        string itemId,
        out string matchableName,
        out Action<Embedding<float>?> setEmbedding)
    {
        matchableName = string.Empty;
        setEmbedding = _ => { };

        if (string.IsNullOrWhiteSpace(itemType) || string.IsNullOrWhiteSpace(itemId))
            return false;

        var snap = _snapshot;
        switch (itemType.Trim().ToLowerInvariant())
        {
            case "entity":
                if (!snap.EntityById.TryGetValue(itemId, out var entity))
                    return false;
                matchableName = entity.FriendlyName;
                setEmbedding = embedding => entity.NameEmbedding = embedding;
                return true;

            case "area":
                if (!snap.AreaById.TryGetValue(itemId, out var area))
                    return false;
                matchableName = area.Name;
                setEmbedding = embedding => area.NameEmbedding = embedding;
                return true;

            case "floor":
                if (!snap.FloorById.TryGetValue(itemId, out var floor))
                    return false;
                matchableName = floor.Name;
                setEmbedding = embedding => floor.NameEmbedding = embedding;
                return true;

            default:
                return false;
        }
    }

    private static Embedding<float> CloneEmbedding(Embedding<float> source) =>
        new(source.Vector.ToArray())
        {
            CreatedAt = source.CreatedAt,
            ModelId = source.ModelId,
            AdditionalProperties = source.AdditionalProperties
        };

    // ── Private: Jinja DTO ──────────────────────────────────────────

    private sealed class JinjaAreaResult
    {
        [JsonPropertyName("area_id")]
        public string AreaId { get; set; } = string.Empty;

        [JsonPropertyName("area_name")]
        public string AreaName { get; set; } = string.Empty;

        [JsonPropertyName("entities")]
        public List<string> Entities { get; set; } = [];
    }

    // ── Private: Immutable snapshot ─────────────────────────────────

    private sealed class LocationSnapshot(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities,
        ImmutableDictionary<string, FloorInfo> floorById,
        ImmutableDictionary<string, AreaInfo> areaById,
        ImmutableDictionary<string, HomeAssistantEntity> entityById)
    {
        public static readonly LocationSnapshot Empty = new(
            ImmutableArray<FloorInfo>.Empty,
            ImmutableArray<AreaInfo>.Empty,
            ImmutableArray<HomeAssistantEntity>.Empty,
            ImmutableDictionary<string, FloorInfo>.Empty,
            ImmutableDictionary<string, AreaInfo>.Empty,
            ImmutableDictionary<string, HomeAssistantEntity>.Empty);

        public ImmutableArray<FloorInfo> Floors { get; } = floors;
        public ImmutableArray<AreaInfo> Areas { get; } = areas;
        public ImmutableArray<HomeAssistantEntity> Entities { get; } = entities;
        public ImmutableDictionary<string, FloorInfo> FloorById { get; } = floorById;
        public ImmutableDictionary<string, AreaInfo> AreaById { get; } = areaById;
        public ImmutableDictionary<string, HomeAssistantEntity> EntityById { get; } = entityById;
    }
}
