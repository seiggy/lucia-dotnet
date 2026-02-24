using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

using lucia.Agents.Models;
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
    private const string EmbeddingsKey = "lucia:location:embeddings";
    private const string LoadedAtKey = "lucia:location:loaded-at";
    private const string VersionKey = "lucia:location:version";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private const double EmbeddingSimilarityThreshold = 0.90;
    private static readonly TimeSpan VersionCheckInterval = TimeSpan.FromSeconds(30);

    private static readonly ActivitySource ActivitySource = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Counter<long> SearchCount = Meter.CreateCounter<long>("entity_location.search.count");
    private static readonly Counter<long> CacheReloads = Meter.CreateCounter<long>("entity_location.cache.reloads");
    private static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>("entity_location.search.duration", "ms");

    private readonly IHomeAssistantClient _haClient;
    private readonly IConnectionMultiplexer _redis;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly ILogger<EntityLocationService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    // Thread-safe: all data is held in an immutable snapshot object swapped atomically
    private volatile LocationSnapshot _snapshot = LocationSnapshot.Empty;
    private long _lastLoadedAtTicks;
    private long _knownVersion;
    private long _lastVersionCheckTicks;

    public DateTimeOffset? LastLoadedAt
    {
        get
        {
            var ticks = Volatile.Read(ref _lastLoadedAtTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public EntityLocationService(
        IHomeAssistantClient haClient,
        IConnectionMultiplexer redis,
        IEmbeddingProviderResolver embeddingResolver,
        ILogger<EntityLocationService> logger)
    {
        _haClient = haClient;
        _redis = redis;
        _embeddingResolver = embeddingResolver;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Try Redis first, fall back to HA
        if (await TryLoadFromRedisAsync(ct).ConfigureAwait(false))
        {
            var snap = _snapshot;
            _logger.LogInformation(
                "Loaded location data from Redis: {FloorCount} floors, {AreaCount} areas, {EntityCount} entities",
                snap.Floors.Length, snap.Areas.Length, snap.Entities.Length);
            return;
        }

        await LoadFromHomeAssistantAsync(ct).ConfigureAwait(false);
    }

    public async Task InvalidateAndReloadAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("InvalidateAndReload");
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync([
                (RedisKey)FloorsKey, (RedisKey)AreasKey, (RedisKey)EntitiesKey,
                (RedisKey)EmbeddingsKey, (RedisKey)LoadedAtKey
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

    public async Task<IReadOnlyList<EntityLocationInfo>> GetEntitiesAsync(CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return _snapshot.Entities;
    }

    public async Task<IReadOnlyList<EntityLocationInfo>> FindEntitiesByLocationAsync(
        string locationName, CancellationToken ct = default)
    {
        return await FindEntitiesByLocationAsync(locationName, domainFilter: null, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityLocationInfo>> FindEntitiesByLocationAsync(
        string locationName, IReadOnlyList<string>? domainFilter, CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        using var activity = ActivitySource.StartActivity("FindEntitiesByLocation");
        activity?.SetTag("search.query", locationName);
        var start = Stopwatch.GetTimestamp();
        SearchCount.Add(1);

        try
        {
            // Capture current snapshot for consistent reads across the entire search
            var snap = _snapshot;
            var areas = snap.Areas;
            var floors = snap.Floors;
            var entities = snap.Entities;

            var matchedAreaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? matchType = null;

            // 1. Exact match on area name
            foreach (var area in areas)
            {
                if (area.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                {
                    matchedAreaIds.Add(area.AreaId);
                    matchType = "exact_area_name";
                }
            }

            // 2. Exact match on area aliases
            if (matchedAreaIds.Count == 0)
            {
                foreach (var area in areas)
                {
                    if (area.Aliases.Any(a => a.Equals(locationName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchedAreaIds.Add(area.AreaId);
                        matchType = "exact_area_alias";
                    }
                }
            }

            // 3. Exact match on floor name → all areas on that floor
            if (matchedAreaIds.Count == 0)
            {
                foreach (var floor in floors)
                {
                    if (floor.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var area in areas)
                        {
                            if (area.FloorId == floor.FloorId)
                                matchedAreaIds.Add(area.AreaId);
                        }
                        matchType = "exact_floor_name";
                    }
                }
            }

            // 4. Exact match on floor aliases → all areas on that floor
            if (matchedAreaIds.Count == 0)
            {
                foreach (var floor in floors)
                {
                    if (floor.Aliases.Any(a => a.Equals(locationName, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var area in areas)
                        {
                            if (area.FloorId == floor.FloorId)
                                matchedAreaIds.Add(area.AreaId);
                        }
                        matchType = "exact_floor_alias";
                    }
                }
            }

            // 5. Substring match on names and aliases
            if (matchedAreaIds.Count == 0)
            {
                foreach (var area in areas)
                {
                    if (area.Name.Contains(locationName, StringComparison.OrdinalIgnoreCase) ||
                        area.Aliases.Any(a => a.Contains(locationName, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchedAreaIds.Add(area.AreaId);
                        matchType = "substring_area";
                    }
                }

                if (matchedAreaIds.Count == 0)
                {
                    foreach (var floor in floors)
                    {
                        if (floor.Name.Contains(locationName, StringComparison.OrdinalIgnoreCase) ||
                            floor.Aliases.Any(a => a.Contains(locationName, StringComparison.OrdinalIgnoreCase)))
                        {
                            foreach (var area in areas)
                            {
                                if (area.FloorId == floor.FloorId)
                                    matchedAreaIds.Add(area.AreaId);
                            }
                            matchType = "substring_floor";
                        }
                    }
                }
            }

            // 6. Embedding similarity ≥ 0.90
            if (matchedAreaIds.Count == 0)
            {
                var embeddingMatches = await FindByEmbeddingSimilarityAsync(locationName, areas, floors, ct)
                    .ConfigureAwait(false);

                foreach (var areaId in embeddingMatches)
                    matchedAreaIds.Add(areaId);

                if (matchedAreaIds.Count > 0)
                    matchType = "embedding";
            }

            activity?.SetTag("search.match_type", matchType ?? "none");
            activity?.SetTag("search.matched_areas", matchedAreaIds.Count);

            if (matchedAreaIds.Count == 0)
            {
                _logger.LogDebug("No location match found for '{LocationName}'", locationName);
                return ImmutableArray<EntityLocationInfo>.Empty;
            }

            // Collect entities from matched areas
            var result = entities
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
            using var activity = ActivitySource.StartActivity("LoadFromHomeAssistant");
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

            // Get authoritative area→entity mapping via Jinja (handles device inheritance)
            var areaEntityMap = await LoadAreaEntityMapAsync(ct).ConfigureAwait(false);

            // Build domain models
            var floors = BuildFloors(floorEntries);
            var areas = BuildAreas(areaEntries, areaEntityMap);
            var entities = BuildEntities(entityEntries, areaEntityMap);

            // Generate embeddings for location names
            var embeddings = await GenerateLocationEmbeddingsAsync(floors, areas, ct).ConfigureAwait(false);

            // Atomic swap of all collections
            SwapData(floors, areas, entities, embeddings);

            _logger.LogInformation(
                "Loaded location data: {FloorCount} floors, {AreaCount} areas, {EntityCount} entities, {EmbeddingCount} embeddings",
                floors.Length, areas.Length, entities.Length, embeddings.Count);

            // Persist to Redis
            await SaveToRedisAsync(ct).ConfigureAwait(false);
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
        return entries.Select(f => new FloorInfo
        {
            FloorId = f.FloorId,
            Name = f.Name,
            Aliases = f.Aliases.AsReadOnly(),
            Level = f.Level,
            Icon = f.Icon
        }).ToImmutableArray();
    }

    private static ImmutableArray<AreaInfo> BuildAreas(
        AreaRegistryEntry[] entries,
        Dictionary<string, List<string>> areaEntityMap)
    {
        return entries.Select(a => new AreaInfo
        {
            AreaId = a.AreaId,
            Name = a.Name,
            FloorId = a.FloorId,
            Aliases = a.Aliases.AsReadOnly(),
            EntityIds = areaEntityMap.TryGetValue(a.AreaId, out var entityIds)
                ? entityIds.AsReadOnly()
                : [],
            Icon = a.Icon,
            Labels = a.Labels.AsReadOnly()
        }).ToImmutableArray();
    }

    private static ImmutableArray<EntityLocationInfo> BuildEntities(
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

        return entries
            .Where(e => e.DisabledBy is null) // Exclude disabled entities
            .Select(e => new EntityLocationInfo
            {
                EntityId = e.EntityId,
                FriendlyName = e.Name ?? e.OriginalName ?? e.EntityId,
                Aliases = e.Aliases.AsReadOnly(),
                // Prefer Jinja area (device-inherited), fall back to registry direct assignment
                AreaId = entityToArea.TryGetValue(e.EntityId, out var jinjaArea)
                    ? jinjaArea
                    : e.AreaId,
                Platform = e.Platform
            }).ToImmutableArray();
    }

    private async Task<ImmutableDictionary<string, Embedding<float>>> GenerateLocationEmbeddingsAsync(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        CancellationToken ct)
    {
        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
        {
            _logger.LogWarning("No embedding provider available — embedding-based location search will be disabled");
            return ImmutableDictionary<string, Embedding<float>>.Empty;
        }

        // Collect all searchable location names (area names, area aliases, floor names, floor aliases)
        var namesToEmbed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var area in areas)
        {
            namesToEmbed.Add(area.Name);
            foreach (var alias in area.Aliases)
                namesToEmbed.Add(alias);
        }

        foreach (var floor in floors)
        {
            namesToEmbed.Add(floor.Name);
            foreach (var alias in floor.Aliases)
                namesToEmbed.Add(alias);
        }

        var builder = ImmutableDictionary.CreateBuilder<string, Embedding<float>>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in namesToEmbed)
        {
            try
            {
                var embedding = await embeddingService.GenerateAsync(name, cancellationToken: ct).ConfigureAwait(false);
                builder[name] = embedding;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for location name '{Name}'", name);
            }
        }

        _logger.LogDebug("Generated {Count} location embeddings", builder.Count);
        return builder.ToImmutable();
    }

    private async Task<IReadOnlyList<string>> FindByEmbeddingSimilarityAsync(
        string query,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<FloorInfo> floors,
        CancellationToken ct)
    {
        var embeddings = _snapshot.Embeddings;
        if (embeddings.IsEmpty)
            return [];

        var embeddingService = await _embeddingResolver.ResolveAsync(ct: ct).ConfigureAwait(false);
        if (embeddingService is null)
            return [];

        Embedding<float> queryEmbedding;
        try
        {
            queryEmbedding = await embeddingService.GenerateAsync(query, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate query embedding for '{Query}'", query);
            return [];
        }

        // Score all location names
        var matches = embeddings
            .Select(kvp => (Name: kvp.Key, Similarity: CosineSimilarity(queryEmbedding, kvp.Value)))
            .Where(m => m.Similarity >= EmbeddingSimilarityThreshold)
            .OrderByDescending(m => m.Similarity)
            .ToList();

        if (matches.Count == 0)
            return [];

        // Resolve matched names back to area IDs
        var areaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in matches)
        {
            // Check if it's an area name or alias
            foreach (var area in areas)
            {
                if (area.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    area.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    areaIds.Add(area.AreaId);
                }
            }

            // Check if it's a floor name or alias → expand to all areas on that floor
            foreach (var floor in floors)
            {
                if (floor.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    floor.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var area in areas)
                    {
                        if (area.FloorId == floor.FloorId)
                            areaIds.Add(area.AreaId);
                    }
                }
            }
        }

        return areaIds.ToList();
    }

    // ── Private: Data Swap ──────────────────────────────────────────

    private void SwapData(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<EntityLocationInfo> entities,
        ImmutableDictionary<string, Embedding<float>> embeddings)
    {
        // Build lookup indexes
        var floorById = floors.ToImmutableDictionary(f => f.FloorId, StringComparer.OrdinalIgnoreCase);
        var areaById = areas.ToImmutableDictionary(a => a.AreaId, StringComparer.OrdinalIgnoreCase);
        var entityById = entities.ToImmutableDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase);

        // Single atomic swap of the entire snapshot — all readers see a consistent view
        _snapshot = new LocationSnapshot(floors, areas, entities, embeddings, floorById, areaById, entityById);
        Volatile.Write(ref _lastLoadedAtTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    // ── Private: Redis Persistence ──────────────────────────────────

    private async Task<bool> TryLoadFromRedisAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();

            var floorsJson = await db.StringGetAsync(FloorsKey).ConfigureAwait(false);
            var areasJson = await db.StringGetAsync(AreasKey).ConfigureAwait(false);
            var entitiesJson = await db.StringGetAsync(EntitiesKey).ConfigureAwait(false);
            var embeddingsJson = await db.StringGetAsync(EmbeddingsKey).ConfigureAwait(false);
            var loadedAtStr = await db.StringGetAsync(LoadedAtKey).ConfigureAwait(false);
            var versionVal = await db.StringGetAsync(VersionKey).ConfigureAwait(false);

            if (floorsJson.IsNullOrEmpty || areasJson.IsNullOrEmpty || entitiesJson.IsNullOrEmpty)
                return false;

            var floors = JsonSerializer.Deserialize<FloorInfo[]>((string)floorsJson!)
                ?.ToImmutableArray() ?? ImmutableArray<FloorInfo>.Empty;
            var areas = JsonSerializer.Deserialize<AreaInfo[]>((string)areasJson!)
                ?.ToImmutableArray() ?? ImmutableArray<AreaInfo>.Empty;
            var entities = JsonSerializer.Deserialize<EntityLocationInfo[]>((string)entitiesJson!)
                ?.ToImmutableArray() ?? ImmutableArray<EntityLocationInfo>.Empty;

            var embeddings = ImmutableDictionary<string, Embedding<float>>.Empty;
            if (!embeddingsJson.IsNullOrEmpty)
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, float[]>>((string)embeddingsJson!);
                if (dict is not null)
                {
                    embeddings = dict.ToImmutableDictionary(
                        kvp => kvp.Key,
                        kvp => new Embedding<float>(kvp.Value),
                        StringComparer.OrdinalIgnoreCase);
                }
            }

            SwapData(floors, areas, entities, embeddings);

            if (!loadedAtStr.IsNullOrEmpty && DateTimeOffset.TryParse((string)loadedAtStr!, out var loadedAt))
            {
                Volatile.Write(ref _lastLoadedAtTicks, loadedAt.UtcTicks);
            }

            // Track Redis version so we know when another instance invalidates the cache
            if (versionVal.TryParse(out long ver))
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
    /// to detect invalidations triggered by the AgentHost. If the version has changed,
    /// reload data from Redis so A2AHost instances stay in sync without their own HA connection.
    /// </summary>
    private async Task EnsureFreshAsync(CancellationToken ct)
    {
        var lastCheck = Volatile.Read(ref _lastVersionCheckTicks);
        if (lastCheck != 0 && Stopwatch.GetElapsedTime(lastCheck) < VersionCheckInterval)
            return;

        // Update check timestamp first to prevent concurrent checks
        Volatile.Write(ref _lastVersionCheckTicks, Stopwatch.GetTimestamp());

        try
        {
            var db = _redis.GetDatabase();
            var versionVal = await db.StringGetAsync(VersionKey).ConfigureAwait(false);

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

    private async Task SaveToRedisAsync(CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var snap = _snapshot;

            var floorsJson = JsonSerializer.Serialize(snap.Floors);
            var areasJson = JsonSerializer.Serialize(snap.Areas);
            var entitiesJson = JsonSerializer.Serialize(snap.Entities);

            // Serialize embeddings as Dictionary<string, float[]> for portability
            var embeddingsDict = snap.Embeddings.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Vector.ToArray());
            var embeddingsJson = JsonSerializer.Serialize(embeddingsDict);

            var batch = db.CreateBatch();
            var tasks = new List<Task>
            {
                batch.StringSetAsync(FloorsKey, floorsJson, CacheTtl),
                batch.StringSetAsync(AreasKey, areasJson, CacheTtl),
                batch.StringSetAsync(EntitiesKey, entitiesJson, CacheTtl),
                batch.StringSetAsync(EmbeddingsKey, embeddingsJson, CacheTtl),
                batch.StringSetAsync(LoadedAtKey, LastLoadedAt?.ToString("O") ?? "", CacheTtl)
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

    // ── Private: Math ───────────────────────────────────────────────

    private static double CosineSimilarity(Embedding<float> vector1, Embedding<float> vector2)
    {
        var span1 = vector1.Vector.Span;
        var span2 = vector2.Vector.Span;

        if (span1.Length != span2.Length)
            return 0.0;

        var dotProduct = 0.0;
        var magnitude1 = 0.0;
        var magnitude2 = 0.0;

        for (var i = 0; i < span1.Length; i++)
        {
            dotProduct += span1[i] * span2[i];
            magnitude1 += span1[i] * span1[i];
            magnitude2 += span2[i] * span2[i];
        }

        if (magnitude1 == 0.0 || magnitude2 == 0.0)
            return 0.0;

        return dotProduct / (Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2));
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
        ImmutableArray<EntityLocationInfo> entities,
        ImmutableDictionary<string, Embedding<float>> embeddings,
        ImmutableDictionary<string, FloorInfo> floorById,
        ImmutableDictionary<string, AreaInfo> areaById,
        ImmutableDictionary<string, EntityLocationInfo> entityById)
    {
        public static readonly LocationSnapshot Empty = new(
            ImmutableArray<FloorInfo>.Empty,
            ImmutableArray<AreaInfo>.Empty,
            ImmutableArray<EntityLocationInfo>.Empty,
            ImmutableDictionary<string, Embedding<float>>.Empty,
            ImmutableDictionary<string, FloorInfo>.Empty,
            ImmutableDictionary<string, AreaInfo>.Empty,
            ImmutableDictionary<string, EntityLocationInfo>.Empty);

        public ImmutableArray<FloorInfo> Floors { get; } = floors;
        public ImmutableArray<AreaInfo> Areas { get; } = areas;
        public ImmutableArray<EntityLocationInfo> Entities { get; } = entities;
        public ImmutableDictionary<string, Embedding<float>> Embeddings { get; } = embeddings;
        public ImmutableDictionary<string, FloorInfo> FloorById { get; } = floorById;
        public ImmutableDictionary<string, AreaInfo> AreaById { get; } = areaById;
        public ImmutableDictionary<string, EntityLocationInfo> EntityById { get; } = entityById;
    }
}
