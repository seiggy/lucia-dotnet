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


namespace lucia.Agents.Services;

/// <summary>
/// Centralized, thread-safe entity location resolution across floors, areas, and entities.
/// All in-memory data is stored in immutable collections swapped atomically via <see cref="Volatile"/>.
/// A snapshot store shares the data across processes; the HA config registry is the source of truth.
/// </summary>
public sealed class EntityLocationService : IEntityLocationService, IAgentFilteredEntityLocationService
{
    private static readonly TimeSpan VersionCheckInterval = TimeSpan.FromSeconds(30);

    private static readonly ActivitySource ActivitySource = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Services.EntityLocation", "1.0.0");
    private static readonly Counter<long> CacheReloads = Meter.CreateCounter<long>("entity_location.cache.reloads");

    private readonly ILocationSnapshotStore _snapshots;
    private readonly IVisibilityConfigStore _visibilityStore;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private readonly LocationSearchService _search;
    private readonly LocationSnapshotLoader _loader;
    private readonly EntityEmbeddingJob _embeddingJob;
    private readonly ILogger<EntityLocationService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly SemaphoreSlim _visibilityGate = new(1, 1);

    private static readonly TimeSpan EmptyCacheRetryInterval = TimeSpan.FromSeconds(30);

    // Thread-safe: all data is held in an immutable snapshot object swapped atomically
    private volatile LocationSnapshot _snapshot = LocationSnapshot.Empty;
    private volatile EntityVisibilityConfig _visibilityConfig = new();
    private volatile bool _loadedExposedOnly;
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
            IsGenerationRunning = _embeddingJob.IsRunning,
            LastLoadedAt = LastLoadedAt
        };

        return Task.FromResult(progress);
    }

    public EntityLocationService(
        ILocationSnapshotStore snapshots,
        IVisibilityConfigStore visibilityStore,
        IEmbeddingProviderResolver embeddingResolver,
        LocationSearchService search,
        LocationSnapshotLoader loader,
        EntityEmbeddingJob embeddingJob,
        ILogger<EntityLocationService> logger)
    {
        _snapshots = snapshots;
        _visibilityStore = visibilityStore;
        _embeddingResolver = embeddingResolver;
        _search = search;
        _loader = loader;
        _embeddingJob = embeddingJob;
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
        _visibilityConfig = await _visibilityStore.LoadAsync(ct).ConfigureAwait(false) ?? new EntityVisibilityConfig();

        // Try the shared store first, fall back to HA
        if (await TryLoadFromStoreAsync(ct).ConfigureAwait(false))
        {
            ApplyVisibilityToEntities();
            var snap = _snapshot;
            _embeddingJob.Start(snap.Floors, snap.Areas, snap.Entities, PersistEmbeddingUpdatesAsync);
            _logger.LogInformation(
                "Loaded location data from the snapshot store: {FloorCount} floors, {AreaCount} areas, {EntityCount} entities",
                snap.Floors.Length, snap.Areas.Length, snap.Entities.Length);
            return;
        }

        await LoadFromHomeAssistantAsync(ct).ConfigureAwait(false);
    }

    public async Task InvalidateAndReloadAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity();
        await _snapshots.InvalidateAsync(ct).ConfigureAwait(false);

        await LoadFromHomeAssistantAsync(ct).ConfigureAwait(false);

        // Bump version so other instances know to reload from the shared store
        await BumpVersionAsync(ct).ConfigureAwait(false);
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
        => FindEntitiesByLocationAsync(locationName, domainFilter: null, LocationSearchService.DefaultOptions, ct);

    public Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName,
        IReadOnlyList<string>? domainFilter,
        CancellationToken ct = default)
        => FindEntitiesByLocationAsync(locationName, domainFilter, LocationSearchService.DefaultOptions, ct);

    public async Task<IReadOnlyList<HomeAssistantEntity>> FindEntitiesByLocationAsync(
        string locationName,
        IReadOnlyList<string>? domainFilter,
        HybridMatchOptions hybridMatchOptions,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return await _search.FindEntitiesByLocationAsync(_snapshot, locationName, domainFilter, hybridMatchOptions, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityMatchResult<HomeAssistantEntity>>> SearchEntitiesAsync(
        string query,
        IReadOnlyList<string>? domainFilter = null,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return await _search.SearchEntitiesAsync(_snapshot, query, domainFilter, options, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityMatchResult<AreaInfo>>> SearchAreasAsync(
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return await _search.SearchAreasAsync(_snapshot, query, options, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EntityMatchResult<FloorInfo>>> SearchFloorsAsync(
        string query,
        HybridMatchOptions? options = null,
        CancellationToken ct = default)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return await _search.SearchFloorsAsync(_snapshot, query, options, ct).ConfigureAwait(false);
    }

    public Task<HierarchicalSearchResult> SearchHierarchyAsync(
        string query,
        HybridMatchOptions? options = null,
        IReadOnlyList<string>? domainFilter = null,
        CancellationToken ct = default) =>
        SearchHierarchyCoreAsync(query, options, domainFilter, callerAgentId: null, ct);

    public Task<HierarchicalSearchResult> SearchHierarchyForAgentAsync(
        string query,
        HybridMatchOptions? options,
        IReadOnlyList<string>? domainFilter,
        string callerAgentId,
        CancellationToken ct = default) =>
        SearchHierarchyCoreAsync(query, options, domainFilter, callerAgentId, ct);

    private async Task<HierarchicalSearchResult> SearchHierarchyCoreAsync(
        string query,
        HybridMatchOptions? options,
        IReadOnlyList<string>? domainFilter,
        string? callerAgentId,
        CancellationToken ct)
    {
        await EnsureFreshAsync(ct).ConfigureAwait(false);
        return await _search.SearchHierarchyAsync(_snapshot, query, options, domainFilter, callerAgentId, ct)
            .ConfigureAwait(false);
    }

    public AreaInfo? GetAreaForEntity(string entityId) => LocationSearchService.GetAreaForEntity(_snapshot, entityId);

    public FloorInfo? GetFloorForArea(string areaId) => LocationSearchService.GetFloorForArea(_snapshot, areaId);

    // ── Synchronous, cache-only fast-path lookups ───────────────────

    /// <inheritdoc />
    public LocationSnapshot GetSnapshot() => _snapshot;

    /// <inheritdoc />
    public bool IsCacheReady => Volatile.Read(ref _lastLoadedAtTicks) != 0;

    /// <inheritdoc />
    public IReadOnlyList<HomeAssistantEntity> ExactMatchEntities(string query, IReadOnlyList<string>? domainFilter = null)
        => LocationSearchService.ExactMatchEntities(_snapshot, query, domainFilter);

    /// <inheritdoc />
    public AreaInfo? ExactMatchArea(string query) => LocationSearchService.ExactMatchArea(_snapshot, query);

    // ── Private: Loading ────────────────────────────────────────────

    private async Task LoadFromHomeAssistantAsync(CancellationToken ct)
    {
        // A load that began before the exposed-only setting changed builds the wrong snapshot, and a caller
        // that waited for it must not return with that snapshot. Load again until the two agree.
        do
        {
            await LoadOnceAsync(ct).ConfigureAwait(false);
        }
        while (_loadedExposedOnly != _visibilityConfig.UseExposedEntitiesOnly);
    }

    private async Task LoadOnceAsync(CancellationToken ct)
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

            var exposedOnly = _visibilityConfig.UseExposedEntitiesOnly;
            var snapshot = await _loader.LoadAsync(exposedOnly, ct).ConfigureAwait(false);
            var floors = snapshot.Floors;
            var areas = snapshot.Areas;
            var entities = snapshot.Entities;

            // Atomic swap of all collections
            _snapshot = snapshot;
            _loadedExposedOnly = exposedOnly;
            Volatile.Write(ref _lastLoadedAtTicks, DateTimeOffset.UtcNow.UtcTicks);

            // Apply per-entity agent visibility from config
            ApplyVisibilityToEntities();

            _logger.LogInformation(
                "Loaded location data: {FloorCount} floors, {AreaCount} areas, {EntityCount} entities",
                floors.Length, areas.Length, entities.Length);

            // Persist to the shared store
            await SaveSnapshotAsync(ct).ConfigureAwait(false);

            // Generate embeddings asynchronously in throttled batches.
            _embeddingJob.Start(floors, areas, entities, PersistEmbeddingUpdatesAsync);
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

    // ── Private: Data Swap ──────────────────────────────────────────

    private void SwapData(
        ImmutableArray<FloorInfo> floors,
        ImmutableArray<AreaInfo> areas,
        ImmutableArray<HomeAssistantEntity> entities)
    {
        _snapshot = LocationSnapshot.Create(floors, areas, entities);
        Volatile.Write(ref _lastLoadedAtTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    // ── Private: Redis Persistence ──────────────────────────────────

    private async Task<bool> TryLoadFromStoreAsync(CancellationToken ct)
    {
        var stored = await _snapshots.TryLoadAsync(ct).ConfigureAwait(false);
        if (stored is null)
            return false;

        _snapshot = stored.Snapshot;
        Volatile.Write(
            ref _lastLoadedAtTicks,
            (stored.LoadedAt ?? DateTimeOffset.UtcNow).UtcTicks);

        if (stored.Version is { } version)
        {
            Volatile.Write(ref _knownVersion, version);
        }
        Volatile.Write(ref _lastVersionCheckTicks, Stopwatch.GetTimestamp());

        return true;
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
            catch (Exception ex) when (!ex.IsCancellation())
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

        var remoteVersion = await _snapshots.GetVersionAsync(ct).ConfigureAwait(false);
        if (remoteVersion is null)
            return;

        var localVersion = Volatile.Read(ref _knownVersion);
        if (remoteVersion <= localVersion)
            return;

        _logger.LogInformation(
            "Location snapshot version changed ({LocalVersion} → {RemoteVersion}), reloading from the store",
            localVersion, remoteVersion);

        if (await TryLoadFromStoreAsync(ct).ConfigureAwait(false))
        {
            CacheReloads.Add(1);
            var snap = _snapshot;
            _embeddingJob.Start(snap.Floors, snap.Areas, snap.Entities, PersistEmbeddingUpdatesAsync);
        }
    }

    private async Task BumpVersionAsync(CancellationToken ct)
    {
        var newVersion = await _snapshots.BumpVersionAsync(ct).ConfigureAwait(false);
        if (newVersion is null)
            return;

        Volatile.Write(ref _knownVersion, newVersion.Value);
        Volatile.Write(ref _lastVersionCheckTicks, Stopwatch.GetTimestamp());
    }

    private async Task PersistEmbeddingUpdatesAsync(CancellationToken ct)
    {
        await SaveSnapshotAsync(ct).ConfigureAwait(false);
        await BumpVersionAsync(ct).ConfigureAwait(false);
    }

    private Task SaveSnapshotAsync(CancellationToken ct)
        => _snapshots.SaveAsync(_snapshot, LastLoadedAt, ct);

    // ── Public: Visibility Configuration ────────────────────────────

    public Task<EntityVisibilityConfig> GetVisibilityConfigAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_visibilityConfig);
    }

    public async Task SetUseExposedOnlyAsync(bool useExposedOnly, CancellationToken ct = default)
    {
        var changed = await UpdateVisibilityAsync(config =>
        {
            if (config.UseExposedEntitiesOnly == useExposedOnly)
                return false;

            config.UseExposedEntitiesOnly = useExposedOnly;
            return true;
        }, ct).ConfigureAwait(false);

        if (!changed)
            return;

        // Changing this flag requires a full HA reload to apply the filter
        await InvalidateAndReloadAsync(ct).ConfigureAwait(false);
    }

    public async Task SetEntityAgentsAsync(Dictionary<string, List<string>?> updates, CancellationToken ct = default)
    {
        await UpdateVisibilityAsync(config =>
        {
            foreach (var (entityId, agents) in updates)
            {
                if (agents is null)
                {
                    // null = visible to all → remove the override
                    config.EntityAgentMap.Remove(entityId);
                }
                else
                {
                    config.EntityAgentMap[entityId] = [.. agents];
                }
            }

            return true;
        }, ct).ConfigureAwait(false);

        ApplyVisibilityToEntities();
        await BumpVersionAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAllAgentFiltersAsync(CancellationToken ct = default)
    {
        await UpdateVisibilityAsync(config =>
        {
            config.EntityAgentMap.Clear();
            return true;
        }, ct).ConfigureAwait(false);

        ApplyVisibilityToEntities();
        await BumpVersionAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a change to a copy of the configuration, saves the copy, and only then publishes it. Updates run
    /// one at a time, a failed save leaves the previous policy in place, and readers never see a dictionary
    /// that is being modified.
    /// </summary>
    private async Task<bool> UpdateVisibilityAsync(Func<EntityVisibilityConfig, bool> change, CancellationToken ct)
    {
        await _visibilityGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _visibilityConfig;
            var updated = new EntityVisibilityConfig
            {
                UseExposedEntitiesOnly = current.UseExposedEntitiesOnly,
                EntityAgentMap = new Dictionary<string, List<string>>(
                    current.EntityAgentMap.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.ToList())),
                    StringComparer.OrdinalIgnoreCase),
            };

            if (!change(updated))
                return false;

            await _visibilityStore.SaveAsync(updated, ct).ConfigureAwait(false);
            _visibilityConfig = updated;
            return true;
        }
        finally
        {
            _visibilityGate.Release();
        }
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
            setEmbedding(EntityEmbeddingJob.Clone(embedding));
            await PersistEmbeddingUpdatesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (!ex.IsCancellation())
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
        await SaveSnapshotAsync(ct).ConfigureAwait(false);
        await BumpVersionAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Removed entity '{EntityId}' from location cache", entityId);
        return true;
    }

    // ── Private: Visibility Persistence ─────────────────────────────

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

    // ── Private: Thread-safe snapshot ────────────────────────────────
}
