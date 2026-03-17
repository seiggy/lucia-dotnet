using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace lucia.Agents.Services;

/// <summary>
/// Centralized, thread-safe entity location resolution across floors, areas, and entities.
/// All in-memory data is stored in immutable collections swapped atomically via <see cref="Volatile"/>.
/// Redis provides 24h persistence; HA config registry is the source of truth.
/// </summary>
public sealed class EntityLocationService : IEntityLocationService
{
    private const string FloorsKey = "lucia:location:floors";
    private const string AreasKey = "lucia:location:areas";
    private const string EntitiesKey = "lucia:location:entities";
    private const string LoadedAtKey = "lucia:location:loaded-at";
    private const string VersionKey = "lucia:location:version";
    private const string VisibilityKey = "lucia:entity-visibility";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan VersionCheckInterval = TimeSpan.FromSeconds(30);
    private const int EmbeddingBatchSize = 25;
    private const int EmbeddingBatchMaxAttempts = 3;
    private static readonly TimeSpan EmbeddingBatchDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan EmbeddingRetryDelay = TimeSpan.FromSeconds(1);

    private static readonly HybridMatchOptions DefaultLocationOptions = new()
    {
        EmbeddingWeight = 0.55,
        ScoreDropoffRatio = 0.9,
        Threshold = 0.2
    };

    private static readonly ActivitySource ActivitySource = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Counter<long> SearchCount = Meter.CreateCounter<long>("entity_location.search.count");
    private static readonly Counter<long> CacheReloads = Meter.CreateCounter<long>("entity_location.cache.reloads");
    private static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>("entity_location.search.duration", "ms");

    private readonly IHomeAssistantClient _haClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly IHybridEntityMatcher _entityMatcher;
    private readonly ILogger<EntityLocationService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly object _embeddingJobGate = new();
    private CancellationTokenSource? _embeddingJobCts;
    private Task? _embeddingJobTask;
    private long _embeddingJobCounter;
    private long _activeEmbeddingJobId;
    private int _isEmbeddingGenerationRunning;

    private static readonly TimeSpan EmptyCacheRetryInterval = TimeSpan.FromSeconds(30);

    // Thread-safe: all data is held in an immutable snapshot object swapped atomically
    private volatile LocationSnapshot _snapshot = LocationSnapshot.Empty;
    private volatile EntityVisibilityConfig _visibilityConfig = new();
    private long _lastLoadedAtTicks;
    private long _knownVersion;
    private long _lastVersionCheckTicks;
    private long _lastEmptyCacheRetryTicks;

    public DateTimeOffset? LastLoadedAt
    {
        get
        {
            var ticks = Volatile.Read(ref _lastLoadedAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public Task<EntityLocationEmbeddingProgress> GetEmbeddingProgressAsync(CancellationToken ct = default)
    {
        var snap = _snapshot;
        var floorGeneratedCount = snap.Floors.Count(f => f.NameEmbedding is not null);
        var areaGeneratedCount = snap.Areas.Count(a => a.NameEmbedding is not null);
        var entityGeneratedCount = snap.Entities.Count(e => e.NameEmbedding is not null);

        var progress = new EntityLocationEmbeddingProgress
        {
            FloorTotalCount = snap.Floors.Length,
            FloorGeneratedCount = floorGeneratedCount,
            AreaTotalCount = snap.Areas.Length,
            AreaGeneratedCount = areaGeneratedCount,
            EntityTotalCount = snap.Entities.Length,
            EntityGeneratedCount = entityGeneratedCount,
            EntityMissingCount = Math.Max(snap.Entities.Length - entityGeneratedCount, 0),
            IsGenerationRunning = Volatile.Read(ref _isEmbeddingGenerationRunning) == 1,
            LastLoadedAt = LastLoadedAt
        };

        return Task.FromResult(progress);
    }

    public EntityLocationService(
        IHomeAssistantClient haClient,
        IConnectionMultiplexer redis,
        IEmbeddingProviderResolver embeddingResolver,
        IHybridEntityMatcher entityMatcher,
        ILogger<EntityLocationService> logger)
    {
        _haClient = haClient;
        _redis = redis;
        _embeddingResolver = embeddingResolver;
        _entityMatcher = entityMatcher;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Guard: skip if data is already loaded (ensures expensive load happens only once)
        if (!_snapshot.Entities.IsEmpty)
        {
            _logger.LogDebug("Entity location cache already initialized ({Count} entities), skipping", _snapshot.Entities.Length);
            return;
        }

        // Always load visibility config first (persisted without TTL)
        await LoadVisibilityConfigAsync().ConfigureAwait(false);

        // Try Redis first, fall back to HA
        if (await TryLoadFromRedisAsync(ct).ConfigureAwait(false))
        {
            ApplyVisibilityToEntities();
            var snap = _snapshot;
            StartEmbeddingGenerationJob(snap.Floors, snap.Areas, snap.Entities);
            _logger.LogInformation(
                "Loaded location data from Redis: {FloorCount} floors, {AreaCount} areas, {EntityCount} entities",
                snap.Floors.Length, snap.Areas.Length, snap.Entities.Length);
            return;
        }

        await LoadFromHomeAssistantAsync(ct).ConfigureAwait(false);
    }

    public async Task InvalidateAndReloadAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity();
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

        await LoadFromHomeAssistantAsync(ct).ConfigureAwait(false);

        // Bump version so other instances (A2AHost) know to reload from Redis
        await BumpRedisVersionAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FloorInfo>> GetFloorsAsync(CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return _snapshot.Floors;
    }

    public async Task<IReadOnlyList<AreaInfo>> GetAreasAsync(CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return _snapshot.Areas;
    }

    public async Task<IReadOnlyList<HomeAssistantEntity>> GetEntitiesAsync(CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
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
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.query", locationName);
        var start = Stopwatch.GetTimestamp();
        SearchCount.Add(1);

        try
        {
            var snap = _snapshot;
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
        string query,
        IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);

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
        await EnsureFreshAsync(ct).ConfigureAwait(false);

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
        await EnsureFreshAsync(ct).ConfigureAwait(false);

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
        await EnsureFreshAsync(ct).ConfigureAwait(false);

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        var opts = options ?? DefaultLocationOptions;
        var snap = _snapshot;

        IReadOnlyList<EntityMatchResult<FloorInfo>> floorMatches = [];
        IReadOnlyList<EntityMatchResult<AreaInfo>> areaMatches = [];
        IReadOnlyList<EntityMatchResult<HomeAssistantEntity>> entityMatches = [];

        // Build the domain-filtered entity list once
        var filteredEntities = domainFilter is { Count: > 0 }
            ? snap.Entities.Where(e => 
                    domainFilter.Contains(e.Domain, StringComparer.OrdinalIgnoreCase) &&
                    (e.IncludeForAgent == null || e.IncludeForAgent.Count > 0)
                )
                .ToList()
            : (IReadOnlyList<HomeAssistantEntity>)snap.Entities;

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
        
        var margin = opts.EmbeddingResolutionMargin; // new option, default 0.30

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
                   + $" exceeds area {bestAreaHybrid?.ToString("F4") ?? "none"}"
                   + $" and floor {bestFloorHybrid?.ToString("F4") ?? "none"}"
                   + $" by margin >{margin:F2}";
            resolvedEntities = entityMatches.Select(m => m.Entity).ToList();
        }
        else if (bestAreaHybrid is not null
            && (bestFloorHybrid is null || bestFloorHybrid - bestAreaHybrid < margin))
        {
            // Area is the best match — expand to all entities in matched areas
            strategy = ResolutionStrategy.Area;
            reason = $"Area path: best area score {bestAreaHybrid:F4}"
                   + $" — entity {bestEntityHybrid?.ToString("F4") ?? "none"}"
                   + $" did not exceed by margin >{margin:F2}";

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

    // ── Private: Loading ────────────────────────────────────────────

    private async Task LoadFromHomeAssistantAsync(CancellationToken ct)
    {
        if (!await _loadLock.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            // Another load is already in progress — wait for it
            await _loadLock.WaitAsync(ct).ConfigureAwait(false);
            _loadLock.Release();
            return;
        }

        try
        {
            using var activity = ActivitySource.StartActivity();
            CacheReloads.Add(1);

            _logger.LogInformation("Loading entity location data from Home Assistant config registries...");

            // Fetch all three registries in parallel
            var floorTask = _haClient.GetFloorRegistryAsync(ct);
            var areaTask = _haClient.GetAreaRegistryAsync(ct);
            var entityTask = _haClient.GetEntityRegistryAsync(ct);

            await Task.WhenAll(floorTask, areaTask, entityTask).ConfigureAwait(false);

            var floorEntries = await floorTask.ConfigureAwait(false);
            var areaEntries = await areaTask.ConfigureAwait(false);
            var entityEntries = await entityTask.ConfigureAwait(false);

            // Optionally filter to only HA-exposed entities
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
                    _logger.LogInformation(
                        "Filtered to exposed entities only: {Before} → {After}",
                        before, entityEntries.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to fetch exposed entity list — loading all entities instead");
                }
            }

            // Get authoritative area→entity mapping via Jinja (handles device inheritance)
            var areaEntityMap = await LoadAreaEntityMapAsync(ct).ConfigureAwait(false);

            // Build domain models
            var floors = BuildFloors(floorEntries);
            var areas = BuildAreas(areaEntries, areaEntityMap);
            var entities = BuildEntities(entityEntries, areaEntityMap);

            // Generate phonetic keys eagerly; embeddings are generated in a throttled background job.
            GeneratePhoneticData(floors, areas, entities);

            // Atomic swap of all collections
            SwapData(floors, areas, entities);

            // Apply per-entity agent visibility from config
            ApplyVisibilityToEntities();

            _logger.LogInformation(
                "Loaded location data: {FloorCount} floors, {AreaCount} areas, {EntityCount} entities",
                floors.Length, areas.Length, entities.Length);

            // Persist to Redis
            await SaveToRedisAsync(ct).ConfigureAwait(false);

            // Generate embeddings asynchronously in throttled batches.
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

    private async Task<Dictionary<string, List<string>>> LoadAreaEntityMapAsync(CancellationToken ct)
    {
        // Jinja template returns JSON: [{"area":"Kitchen","entities":["light.kitchen",...]},...]
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
            {
                map[entry.AreaId] = entry.Entities;
            }
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load area→entity map via Jinja template, falling back to registry area_id only");
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static ImmutableArray<FloorInfo> BuildFloors(FloorRegistryEntry[] entries)
    {
        return [
            ..entries.Select(f => new FloorInfo
            {
                FloorId = f.FloorId,
                Aliases = EntityMatchNameFormatter.SanitizeAliases(f.Aliases).AsReadOnly(),
                Name = EntityMatchNameFormatter.ResolveName(
                    f.Name,
                    f.Aliases,
                    f.FloorId),
                Level = f.Level,
                Icon = f.Icon
            })
        ];
    }

    private static ImmutableArray<AreaInfo> BuildAreas(
        AreaRegistryEntry[] entries,
        Dictionary<string, List<string>> areaEntityMap)
    {
        return [
            ..entries.Select(a => new AreaInfo
            {
                AreaId = a.AreaId,
                Name = EntityMatchNameFormatter.ResolveName(
                    a.Name,
                    a.Aliases,
                    a.AreaId),
                FloorId = a.FloorId,
                Aliases = EntityMatchNameFormatter.SanitizeAliases(a.Aliases).AsReadOnly(),
                EntityIds = areaEntityMap.TryGetValue(a.AreaId, out var entityIds)
                    ? entityIds.AsReadOnly()
                    : [],
                Icon = a.Icon,
                Labels = a.Labels.AsReadOnly()
            })
        ];
    }

    private static ImmutableArray<HomeAssistantEntity> BuildEntities(
        EntityRegistryEntry[] entries,
        Dictionary<string, List<string>> areaEntityMap)
    {
        // Build reverse lookup: entity_id → area_id from Jinja (authoritative)
        var entityToArea = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (areaId, entityIds) in areaEntityMap)
        {
            foreach (var entityId in entityIds)
            {
                entityToArea.TryAdd(entityId, areaId);
            }
        }

        return [
            ..entries
                .Where(e => e.DisabledBy is null) // Exclude disabled entities
                .Select(e => new HomeAssistantEntity
                {
                    EntityId = e.EntityId,
                    FriendlyName = EntityMatchNameFormatter.ResolveName(
                        e.Name ?? e.OriginalName,
                        e.Aliases,
                        e.EntityId,
                        stripDomainFromId: true),
                    Aliases = EntityMatchNameFormatter.SanitizeAliases(e.Aliases).AsReadOnly(),
                    // Prefer Jinja area (device-inherited), fall back to registry direct assignment
                    AreaId = entityToArea.TryGetValue(e.EntityId, out var jinjaArea)
                        ? jinjaArea
                        : e.AreaId,
                    Platform = e.Platform,
                    SupportedFeatures = (SupportedFeaturesFlags)e.SupportedFeatures
                })
        ];
    }

    private void GeneratePhoneticData(
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
            _embeddingJobTask = Task.Run(
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
                _logger.LogWarning("No embedding provider available — embedding-based search will be disabled");
                return;
            }

            var namesToEmbed = new Dictionary<string, List<Action<Embedding<float>>>>(StringComparer.OrdinalIgnoreCase);

            void RegisterForEmbedding(string name, Action<Embedding<float>> setter, bool hasEmbedding)
            {
                if (hasEmbedding || string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                if (!namesToEmbed.TryGetValue(name, out var setters))
                {
                    setters = [];
                    namesToEmbed[name] = setters;
                }

                setters.Add(setter);
            }

            foreach (var floor in floors)
            {
                RegisterForEmbedding(floor.Name, e => floor.NameEmbedding = e, floor.NameEmbedding is not null);
            }

            foreach (var area in areas)
            {
                RegisterForEmbedding(area.Name, e => area.NameEmbedding = e, area.NameEmbedding is not null);
            }

            foreach (var entity in entities)
            {
                RegisterForEmbedding(entity.FriendlyName, e => entity.NameEmbedding = e, entity.NameEmbedding is not null);
            }

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
                {
                    continue;
                }

                var count = Math.Min(batchNames.Length, batchEmbeddings.Count);
                for (var i = 0; i < count; i++)
                {
                    if (!namesToEmbed.TryGetValue(batchNames[i], out var setters))
                    {
                        continue;
                    }

                    foreach (var setter in setters)
                    {
                        setter(CloneEmbedding(batchEmbeddings[i]));
                    }

                    generated++;
                }

                await PersistEmbeddingUpdatesAsync(ct).ConfigureAwait(false);

                if (offset + EmbeddingBatchSize < names.Count)
                {
                    await Task.Delay(EmbeddingBatchDelay, ct).ConfigureAwait(false);
                }
            }

            _logger.LogInformation(
                "Generated {GeneratedCount} embeddings for location snapshot (requested {RequestedCount})",
                generated,
                namesToEmbed.Count);
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
            {
                Volatile.Write(ref _isEmbeddingGenerationRunning, 0);
            }
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
                _logger.LogWarning(
                    ex,
                    "Embedding batch failed on attempt {Attempt}/{MaxAttempts}; retrying in {DelayMs}ms",
                    attempt,
                    EmbeddingBatchMaxAttempts,
                    EmbeddingRetryDelay.TotalMilliseconds);
                await Task.Delay(EmbeddingRetryDelay, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Embedding batch failed after {MaxAttempts} attempts for {ItemCount} items",
                    EmbeddingBatchMaxAttempts,
                    batchNames.Length);
                return null;
            }
        }

        return null;
    }
    
    private static Embedding<float> CloneEmbedding(Embedding<float> source)
    {
        var cloned = source.Vector.ToArray();
        return new Embedding<float>(cloned)
        {
            CreatedAt = source.CreatedAt,
            ModelId = source.ModelId,
            AdditionalProperties = source.AdditionalProperties
        };
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

    // ── Private: Data Swap ──────────────────────────────────────────

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

    // ── Private: Redis Persistence ──────────────────────────────────

    private async Task<bool> TryLoadFromRedisAsync(CancellationToken ct)
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
                return false;

            var floors = JsonSerializer.Deserialize<FloorInfo[]>((string)floorsResult!)
                ?.ToImmutableArray() ?? ImmutableArray<FloorInfo>.Empty;
            var areas = JsonSerializer.Deserialize<AreaInfo[]>((string)areasResult!)
                ?.ToImmutableArray() ?? ImmutableArray<AreaInfo>.Empty;
            var entities = JsonSerializer.Deserialize<HomeAssistantEntity[]>((string)entitiesResult!)
                ?.ToImmutableArray() ?? ImmutableArray<HomeAssistantEntity>.Empty;

            SwapData(floors, areas, entities);

            if (!loadedAtResult.IsNullOrEmpty && DateTimeOffset.TryParse((string)loadedAtResult!, out var loadedAt))
            {
                Volatile.Write(ref _lastLoadedAtTicks, loadedAt.UtcTicks);
            }

            if (versionResult.TryParse(out long ver))
            {
                Volatile.Write(ref _knownVersion, ver);
            }
            Volatile.Write(ref _lastVersionCheckTicks, Stopwatch.GetTimestamp());

            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to load location data from Redis, will load from Home Assistant");
            return false;
        }
    }

    /// <summary>
    /// Distributed cache freshness check: periodically reads the Redis version counter
    /// to detect invalidations triggered by the AgentHost.
    /// </summary>
    private async Task EnsureFreshAsync(CancellationToken ct)
    {
        if (_snapshot.Entities.IsEmpty)
        {
            // Throttle empty-cache retries to avoid flooding HA on every query
            var lastRetry = Volatile.Read(ref _lastEmptyCacheRetryTicks);
            if (lastRetry != 0 && Stopwatch.GetElapsedTime(lastRetry) < EmptyCacheRetryInterval)
                return;

            Volatile.Write(ref _lastEmptyCacheRetryTicks, Stopwatch.GetTimestamp());

            try
            {
                _logger.LogInformation("Entity location cache is empty, attempting to load from Home Assistant...");
                await InitializeAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-reload of empty entity location cache failed — will retry in {Interval}s",
                    EmptyCacheRetryInterval.TotalSeconds);
            }

            return;
        }

        var lastCheck = Volatile.Read(ref _lastVersionCheckTicks);
        if (lastCheck != 0 && Stopwatch.GetElapsedTime(lastCheck) < VersionCheckInterval)
            return;

        Volatile.Write(ref _lastVersionCheckTicks, Stopwatch.GetTimestamp());

        try
        {
            var db = _redis.GetDatabase();
            var versionVal = await db.StringGetSetExpiryAsync(VersionKey, CacheTtl).ConfigureAwait(false);

            if (!versionVal.TryParse(out long remoteVersion))
                return;

            var localVersion = Volatile.Read(ref _knownVersion);
            if (remoteVersion <= localVersion)
                return;

            _logger.LogInformation(
                "Redis location cache version changed ({LocalVersion} → {RemoteVersion}), reloading from Redis",
                localVersion, remoteVersion);

            if (await TryLoadFromRedisAsync(ct).ConfigureAwait(false))
            {
                CacheReloads.Add(1);
                var snap = _snapshot;
                StartEmbeddingGenerationJob(snap.Floors, snap.Areas, snap.Entities);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to check Redis location cache version, using current in-memory data");
        }
    }

    private async Task BumpRedisVersionAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var newVersion = await db.StringIncrementAsync(VersionKey).ConfigureAwait(false);
            Volatile.Write(ref _knownVersion, newVersion);
            Volatile.Write(ref _lastVersionCheckTicks, Stopwatch.GetTimestamp());

            _logger.LogDebug("Bumped Redis location cache version to {Version}", newVersion);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to bump Redis location cache version");
        }
    }

    private async Task PersistEmbeddingUpdatesAsync(CancellationToken ct)
    {
        await SaveToRedisAsync(ct).ConfigureAwait(false);
        await BumpRedisVersionAsync().ConfigureAwait(false);
    }

    private async Task SaveToRedisAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var snap = _snapshot;

            var floorsJson = JsonSerializer.Serialize(snap.Floors);
            var areasJson = JsonSerializer.Serialize(snap.Areas);
            var entitiesJson = JsonSerializer.Serialize(snap.Entities);

            var batch = db.CreateBatch();
            var tasks = new List<Task>
            {
                batch.StringSetAsync(FloorsKey, floorsJson, CacheTtl, flags: CommandFlags.FireAndForget),
                batch.StringSetAsync(AreasKey, areasJson, CacheTtl, flags: CommandFlags.FireAndForget),
                batch.StringSetAsync(EntitiesKey, entitiesJson, CacheTtl, flags: CommandFlags.FireAndForget),
                batch.StringSetAsync(LoadedAtKey, LastLoadedAt?.ToString("O") ?? "", CacheTtl, flags: CommandFlags.FireAndForget)
            };
            batch.Execute();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            _logger.LogDebug("Saved entity location data to Redis");
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to save location data to Redis — data is available in-memory only");
        }
    }

    // ── Public: Visibility Configuration ────────────────────────────

    public Task<EntityVisibilityConfig> GetVisibilityConfigAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_visibilityConfig);
    }

    public async Task SetUseExposedOnlyAsync(bool useExposedOnly, CancellationToken ct = default)
    {
        var config = _visibilityConfig;
        if (config.UseExposedEntitiesOnly == useExposedOnly)
            return;

        config.UseExposedEntitiesOnly = useExposedOnly;
        await SaveVisibilityConfigAsync().ConfigureAwait(false);

        // Changing this flag requires a full HA reload to apply the filter
        await InvalidateAndReloadAsync(ct).ConfigureAwait(false);
    }

    public async Task SetEntityAgentsAsync(Dictionary<string, List<string>?> updates, CancellationToken ct = default)
    {
        var config = _visibilityConfig;

        foreach (var (entityId, agents) in updates)
        {
            if (agents is null)
            {
                // null = visible to all → remove the override
                config.EntityAgentMap.Remove(entityId);
            }
            else
            {
                config.EntityAgentMap[entityId] = agents;
            }
        }

        await SaveVisibilityConfigAsync().ConfigureAwait(false);
        ApplyVisibilityToEntities();
        await BumpRedisVersionAsync().ConfigureAwait(false);
    }

    public async Task ClearAllAgentFiltersAsync(CancellationToken ct = default)
    {
        _visibilityConfig.EntityAgentMap.Clear();
        await SaveVisibilityConfigAsync().ConfigureAwait(false);
        ApplyVisibilityToEntities();
        await BumpRedisVersionAsync().ConfigureAwait(false);
    }

    public async Task<bool> EvictEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default)
    {
        if (!TryResolveEmbeddingTarget(itemType, itemId, out _, out var setEmbedding))
        {
            return false;
        }

        setEmbedding(null);
        await PersistEmbeddingUpdatesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RegenerateEmbeddingAsync(string itemType, string itemId, CancellationToken ct = default)
    {
        if (!TryResolveEmbeddingTarget(itemType, itemId, out var matchableName, out var setEmbedding))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(matchableName))
        {
            return false;
        }

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
        {
            return false;
        }

        try
        {
            var embedding = await embeddingService
                .GenerateAsync(matchableName, cancellationToken: ct)
                .ConfigureAwait(false);
            setEmbedding(CloneEmbedding(embedding));
            await PersistEmbeddingUpdatesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to regenerate embedding for {ItemType} '{ItemId}'",
                itemType,
                itemId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveEntityAsync(string entityId, CancellationToken ct = default)
    {
        var snap = _snapshot;
        if (!snap.EntityById.TryGetValue(entityId, out var entity))
            return false;

        var newEntities = snap.Entities.Remove(entity);
        SwapData(snap.Floors, snap.Areas, newEntities);
        await SaveToRedisAsync(ct).ConfigureAwait(false);
        await BumpRedisVersionAsync().ConfigureAwait(false);

        _logger.LogInformation("Removed entity '{EntityId}' from location cache", entityId);
        return true;
    }

    // ── Private: Visibility Persistence ─────────────────────────────

    private async Task LoadVisibilityConfigAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync(VisibilityKey).ConfigureAwait(false);
            if (!json.IsNullOrEmpty)
            {
                _visibilityConfig = JsonSerializer.Deserialize<EntityVisibilityConfig>((string)json!)
                    ?? new EntityVisibilityConfig();
            }
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to load entity visibility config from Redis, using defaults");
        }
    }

    private async Task SaveVisibilityConfigAsync()
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(_visibilityConfig);
            // No TTL — user configuration persists indefinitely
            await db.StringSetAsync(VisibilityKey, json).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to save entity visibility config to Redis");
        }
    }

    /// <summary>
    /// Applies the <see cref="_visibilityConfig"/> agent mappings to all in-memory entities.
    /// </summary>
    private void ApplyVisibilityToEntities()
    {
        var config = _visibilityConfig;
        var snap = _snapshot;

        foreach (var entity in snap.Entities)
        {
            entity.IncludeForAgent = config.EntityAgentMap.TryGetValue(entity.EntityId, out var agents)
                ? new HashSet<string>(agents, StringComparer.OrdinalIgnoreCase)
                : null; // null = visible to all
        }
    }

    private bool TryResolveEmbeddingTarget(
        string itemType,
        string itemId,
        out string matchableName,
        out Action<Embedding<float>?> setEmbedding)
    {
        matchableName = string.Empty;
        setEmbedding = _ => { };

        if (string.IsNullOrWhiteSpace(itemType) || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        var snap = _snapshot;
        switch (itemType.Trim().ToLowerInvariant())
        {
            case "entity":
                if (!snap.EntityById.TryGetValue(itemId, out var entity))
                {
                    return false;
                }

                matchableName = entity.FriendlyName;
                setEmbedding = embedding => entity.NameEmbedding = embedding;
                return true;

            case "area":
                if (!snap.AreaById.TryGetValue(itemId, out var area))
                {
                    return false;
                }

                matchableName = area.Name;
                setEmbedding = embedding => area.NameEmbedding = embedding;
                return true;

            case "floor":
                if (!snap.FloorById.TryGetValue(itemId, out var floor))
                {
                    return false;
                }

                matchableName = floor.Name;
                setEmbedding = embedding => floor.NameEmbedding = embedding;
                return true;

            default:
                return false;
        }
    }

    // ── Private: Jinja DTOs ─────────────────────────────────────────

    private sealed class JinjaAreaResult
    {
        [System.Text.Json.Serialization.JsonPropertyName("area_id")]
        public string AreaId { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("area_name")]
        public string AreaName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("entities")]
        public List<string> Entities { get; set; } = [];
    }

    // ── Private: Thread-safe snapshot ────────────────────────────────

    /// <summary>
    /// Immutable snapshot of all location data. Swapped atomically as a single reference
    /// so all reader threads see a consistent view without locks.
    /// </summary>
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
