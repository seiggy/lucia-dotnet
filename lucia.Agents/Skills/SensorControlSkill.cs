using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for querying sensor and binary_sensor entities in Home Assistant.
/// Sensors are read-only — this skill provides discovery and state-reading tools only.
/// </summary>
public sealed class SensorControlSkill : IAgentSkill, IOptimizableSkill, ICommandPatternProvider
{
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingService;
    private readonly ILogger<SensorControlSkill> _logger;
    private readonly IDeviceCacheService _deviceCache;
    private readonly IEntityLocationService _locationService;
    private readonly IHybridEntityMatcher _entityMatcher;
    private readonly IOptionsMonitor<SensorControlSkillOptions> _options;
    private ImmutableArray<SensorEntity> _cachedSensors = [];
    private long _lastCacheUpdateTicks = DateTime.MinValue.Ticks;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.SensorControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.SensorControl", "1.0.0");
    private static readonly Counter<long> SensorSearchRequests = Meter.CreateCounter<long>("sensor.search.requests");
    private static readonly Counter<long> SensorSearchSuccess = Meter.CreateCounter<long>("sensor.search.success");
    private static readonly Counter<long> SensorSearchFailures = Meter.CreateCounter<long>("sensor.search.failures");
    private static readonly Histogram<double> SensorSearchDurationMs = Meter.CreateHistogram<double>("sensor.search.duration", "ms");
    private static readonly Histogram<double> CacheRefreshDurationMs = Meter.CreateHistogram<double>("sensor.cache.refresh.duration", "ms");

    public SensorControlSkill(
        IHomeAssistantClient homeAssistantClient,
        IEmbeddingProviderResolver embeddingResolver,
        ILogger<SensorControlSkill> logger,
        IDeviceCacheService deviceCache,
        IEntityLocationService locationService,
        IHybridEntityMatcher entityMatcher,
        IOptionsMonitor<SensorControlSkillOptions> options)
    {
        _homeAssistantClient = homeAssistantClient;
        _embeddingResolver = embeddingResolver;
        _logger = logger;
        _deviceCache = deviceCache;
        _locationService = locationService;
        _entityMatcher = entityMatcher;
        _options = options;
    }

    public IList<AITool> GetTools()
    {
        return [
            AIFunctionFactory.Create(FindSensorAsync),
            AIFunctionFactory.Create(FindSensorsByAreaAsync),
            AIFunctionFactory.Create(GetSensorStateAsync),
            AIFunctionFactory.Create(GetBinarySensorStateAsync),
            AIFunctionFactory.Create(GetAreaSensorsAsync)
        ];
    }

    public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
    [
        new()
        {
            Id = "sensor-query",
            SkillId = "SensorControlSkill",
            Action = "query",
            Templates =
            [
                "what [is] [the] {entity} [reading|value]",
                "how [much|many] {entity} [is there]",
                "is [the] {entity} [open|closed|on|off]",
            ],
        },
    ];

    // ── IOptimizableSkill ─────────────────────────────────────────

    /// <inheritdoc/>
    public string SkillDisplayName => "Sensor Control";

    /// <inheritdoc/>
    public string SkillId => "sensor-control";

    /// <inheritdoc/>
    public string AgentId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<string> SearchToolNames { get; } = ["FindSensor", "FindSensorsByArea"];

    /// <inheritdoc/>
    public string ConfigSectionName => SensorControlSkillOptions.SectionName;

    /// <inheritdoc/>
    public IReadOnlyList<string> EntityDomains => _options.CurrentValue.EntityDomains;

    /// <inheritdoc/>
    public HybridMatchOptions GetCurrentMatchOptions()
    {
        var opts = _options.CurrentValue;
        return new HybridMatchOptions
        {
            Threshold = opts.HybridSimilarityThreshold,
            EmbeddingWeight = opts.EmbeddingWeight,
            ScoreDropoffRatio = opts.ScoreDropoffRatio,
            DisagreementPenalty = opts.DisagreementPenalty,
            EmbeddingResolutionMargin = opts.EmbeddingResolutionMargin
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing SensorControlSkill and caching sensor entities...");

        _embeddingService = await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
        if (_embeddingService is null)
        {
            _logger.LogWarning("No embedding provider configured — sensor semantic search will not be available.");
            return;
        }

        await RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SensorControlSkill initialized with {SensorCount} sensor entities", _cachedSensors.Length);
    }

    /// <summary>
    /// Re-resolves the embedding generator using the specified provider name.
    /// Called by the owning agent when the embedding configuration changes.
    /// </summary>
    public async Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
    {
        _embeddingService = await _embeddingResolver.ResolveAsync(providerName, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SensorControlSkill: embedding provider updated to '{Provider}'", providerName ?? "system-default");
    }

    [Description("Find a sensor by name or description using natural language. Works for both regular sensors (temperature, humidity, etc.) and binary sensors (motion, door, etc.).")]
    public async Task<string> FindSensorAsync(
        [Description("Name or description of the sensor (e.g., 'living room temperature', 'front door', 'motion sensor', 'battery level')")] string searchTerm)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        if (_cachedSensors.IsEmpty)
        {
            SensorSearchFailures.Add(1);
            return "No sensors available in the system.";
        }

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.term", searchTerm);
        var start = Stopwatch.GetTimestamp();
        SensorSearchRequests.Add(1);

        try
        {
            if (_embeddingService is null)
            {
                SensorSearchFailures.Add(1);
                return "Embedding provider not available for sensor search.";
            }

            var opts = _options.CurrentValue;
            var matchOptions = new HybridMatchOptions
            {
                Threshold = opts.HybridSimilarityThreshold,
                EmbeddingWeight = opts.EmbeddingWeight,
                ScoreDropoffRatio = opts.ScoreDropoffRatio,
                DisagreementPenalty = opts.DisagreementPenalty,
                EmbeddingResolutionMargin = opts.EmbeddingResolutionMargin
            };

            var matches = await _entityMatcher.FindMatchesAsync(
                searchTerm,
                (IReadOnlyList<SensorEntity>)_cachedSensors,
                _embeddingService,
                matchOptions).ConfigureAwait(false);

            if (matches.Count > 0)
            {
                activity?.SetTag("match.type", "sensor");
                activity?.SetTag("match.count", matches.Count);
                activity?.SetTag("match.top_similarity", matches[0].HybridScore);

                var sb = new StringBuilder();
                if (matches.Count == 1)
                    sb.Append("Found sensor: ");
                else
                    sb.AppendLine($"Found {matches.Count} matching sensor(s):");

                foreach (var match in matches)
                {
                    var sensor = match.Entity;
                    var state = await _homeAssistantClient.GetEntityStateAsync(sensor.EntityId).ConfigureAwait(false);
                    if (state is null) continue;

                    var stateInfo = FormatSensorState(state, sensor);

                    if (matches.Count == 1)
                        sb.Append($"{sensor.FriendlyName} (Entity ID: {sensor.EntityId}), {stateInfo}");
                    else
                        sb.AppendLine($"- {sensor.FriendlyName} (Entity ID: {sensor.EntityId}), {stateInfo}");
                }

                SensorSearchSuccess.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return matches.Count == 1 ? sb.ToString() : sb.ToString().TrimEnd();
            }

            // Fallback: use hierarchical search for area-based resolution
            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                searchTerm, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);
            var locationEntities = hierarchyResult.ResolvedEntities;

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy != ResolutionStrategy.None && locationEntities.Count > 0)
            {
                var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var areaSensors = _cachedSensors.Where(s => matchedEntityIds.Contains(s.EntityId)).ToList();

                if (areaSensors.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Found {areaSensors.Count} sensor(s) matching '{searchTerm}':");

                    foreach (var sensor in areaSensors)
                    {
                        var state = await _homeAssistantClient.GetEntityStateAsync(sensor.EntityId).ConfigureAwait(false);
                        if (state is null) continue;
                        sb.AppendLine($"- {sensor.FriendlyName} (Entity ID: {sensor.EntityId}), {FormatSensorState(state, sensor)}");
                    }

                    SensorSearchSuccess.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return sb.ToString().TrimEnd();
                }
            }

            SensorSearchFailures.Add(1);
            return $"No sensor found matching '{searchTerm}'. Available sensors: {string.Join(", ", _cachedSensors.Take(5).Select(s => s.FriendlyName))}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding sensor for search term: {SearchTerm}", searchTerm);
            SensorSearchFailures.Add(1);
            return $"Error searching for sensor: {ex.Message}";
        }
        finally
        {
            SensorSearchDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Find all sensors in a specific area/room. Returns both regular sensors and binary sensors.")]
    public async Task<string> FindSensorsByAreaAsync(
        [Description("The area/room name to search (e.g., 'living room', 'garage', 'kitchen')")] string areaName)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        if (_cachedSensors.IsEmpty)
            return "No sensors available in the system.";

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);

        try
        {
            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                areaName, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);
            var locationEntities = hierarchyResult.ResolvedEntities;

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy == ResolutionStrategy.None || locationEntities.Count == 0)
            {
                var availableAreas = _cachedSensors
                    .Where(s => !string.IsNullOrEmpty(s.Area))
                    .Select(s => s.Area!)
                    .Distinct();
                return $"No area found matching '{areaName}'. {hierarchyResult.ResolutionReason}. Available areas with sensors: {string.Join(", ", availableAreas)}";
            }

            var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaSensors = _cachedSensors.Where(s => matchedEntityIds.Contains(s.EntityId)).ToList();
            if (!areaSensors.Any())
                return $"No sensors found in the matched location for '{areaName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {areaSensors.Count} sensor(s) matching '{areaName}':");

            foreach (var sensor in areaSensors)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(sensor.EntityId).ConfigureAwait(false);
                if (state is null) continue;
                sb.AppendLine($"- {sensor.FriendlyName} (Entity ID: {sensor.EntityId}), {FormatSensorState(state, sensor)}");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding sensors in area: {AreaName}", areaName);
            return $"Error searching for sensors in area: {ex.Message}";
        }
    }

    [Description("Get the current reading of a specific sensor entity. Use this for regular sensors (temperature, humidity, battery, power, etc.). Sensors are read-only.")]
    public async Task<string> GetSensorStateAsync(
        [Description("The entity ID of the sensor (e.g., 'sensor.living_room_temperature', 'sensor.battery_level')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        try
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Sensor '{entityId}' not found.";

            var device = _cachedSensors.FirstOrDefault(s => s.EntityId == entityId);
            return FormatSensorState(state, device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sensor state for {EntityId}", entityId);
            return $"Error getting state for '{entityId}': {ex.Message}";
        }
    }

    [Description("Get the current state of a specific binary sensor entity. Binary sensors have on/off states (e.g., motion detected, door open/closed, window open). They are read-only.")]
    public async Task<string> GetBinarySensorStateAsync(
        [Description("The entity ID of the binary sensor (e.g., 'binary_sensor.front_door', 'binary_sensor.living_room_motion')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        try
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Binary sensor '{entityId}' not found.";

            var device = _cachedSensors.FirstOrDefault(s => s.EntityId == entityId);

            var friendlyName = state.Attributes.TryGetValue("friendly_name", out var fn) ? fn?.ToString() : entityId;
            var deviceClass = state.Attributes.TryGetValue("device_class", out var dc) ? dc?.ToString() : null;

            var sb = new StringBuilder();
            sb.Append($"State: {state.State}");

            if (deviceClass is not null)
                sb.Append($", Type: {deviceClass}");

            if (device is not null)
                sb.Append($", Area: {device.Area ?? "unknown"}");

            sb.Append($", Name: {friendlyName}");

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting binary sensor state for {EntityId}", entityId);
            return $"Error getting state for '{entityId}': {ex.Message}";
        }
    }

    [Description("Get all sensors of a specific type in an area. Filter by device class to find specific kinds of sensors (e.g., temperature sensors, motion sensors, battery sensors).")]
    public async Task<string> GetAreaSensorsAsync(
        [Description("The area/room name to search (e.g., 'living room', 'kitchen')")] string areaName,
        [Description("Optional device class filter (e.g., 'temperature', 'humidity', 'motion', 'door', 'window', 'battery', 'power', 'illuminance'). Leave empty to return all sensors in the area.")] string? deviceClass = null)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        if (_cachedSensors.IsEmpty)
            return "No sensors available in the system.";

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);
        activity?.SetTag("filter.device_class", deviceClass ?? "none");

        try
        {
            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                areaName, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy == ResolutionStrategy.None || hierarchyResult.ResolvedEntities.Count == 0)
                return $"No area found matching '{areaName}'. {hierarchyResult.ResolutionReason}";

            var matchedEntityIds = hierarchyResult.ResolvedEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaSensors = _cachedSensors.Where(s => matchedEntityIds.Contains(s.EntityId)).ToList();

            if (!string.IsNullOrEmpty(deviceClass))
            {
                areaSensors = areaSensors.Where(s =>
                    s.DeviceClass?.Equals(deviceClass, StringComparison.OrdinalIgnoreCase) == true
                    || s.EntityId.Contains(deviceClass, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (areaSensors.Count == 0)
            {
                if (!string.IsNullOrEmpty(deviceClass))
                    return $"No {deviceClass} sensors found in '{areaName}'.";
                return $"No sensors found in '{areaName}'.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {areaSensors.Count} sensor(s) in '{areaName}'{(deviceClass is not null ? $" (type: {deviceClass})" : "")}:");

            foreach (var sensor in areaSensors)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(sensor.EntityId).ConfigureAwait(false);
                if (state is null) continue;

                var unit = sensor.UnitOfMeasurement ?? "";
                var sensorType = sensor.IsBinarySensor ? "binary_sensor" : "sensor";
                var dc = sensor.DeviceClass ?? "unknown";
                sb.AppendLine($"- {sensor.FriendlyName}: {state.State}{unit} [{sensorType}/{dc}] (Entity ID: {sensor.EntityId})");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding sensors in area: {AreaName}", areaName);
            return $"Error searching for sensors in area: {ex.Message}";
        }
    }

    private static string FormatSensorState(HomeAssistantState state, SensorEntity? device)
    {
        var sb = new StringBuilder();

        if (device is not null && device.IsBinarySensor)
        {
            // Binary sensor formatting
            sb.Append($"State: {state.State}");
            var deviceClass = state.Attributes.TryGetValue("device_class", out var dc) ? dc?.ToString() : null;
            if (deviceClass is not null)
                sb.Append($", Type: {deviceClass}");
            sb.Append($", Name: {device.FriendlyName}");
            if (device.Area is not null)
                sb.Append($", Area: {device.Area}");
        }
        else
        {
            // Regular sensor formatting
            var unit = state.Attributes.TryGetValue("unit_of_measurement", out var u) ? u?.ToString() : "";
            var friendlyName = state.Attributes.TryGetValue("friendly_name", out var fn) ? fn?.ToString() : state.EntityId;
            var deviceClass = state.Attributes.TryGetValue("device_class", out var dc) ? dc?.ToString() : null;

            sb.Append($"State: {state.State}{unit}");

            if (deviceClass is not null)
                sb.Append($", Type: {deviceClass}");

            if (device is not null)
                sb.Append($", Area: {device.Area ?? "unknown"}");

            sb.Append($", Name: {friendlyName}");
        }

        return sb.ToString();
    }

    private async Task RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_embeddingService is null)
        {
            _logger.LogWarning("Skipping sensor cache refresh — no embedding provider available.");
            return;
        }

        using var activity = ActivitySource.StartActivity();
        var start = Stopwatch.GetTimestamp();
        try
        {
            // Try Redis cache first (device data only — areas come from IEntityLocationService)
            var cached = await _deviceCache.GetCachedSensorsAsync(cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                var allEmbeddingsFound = true;
                foreach (var sensor in cached)
                {
                    var embedding = await _deviceCache.GetEmbeddingAsync($"sensor:{sensor.EntityId}", cancellationToken).ConfigureAwait(false);
                    if (embedding is not null)
                    {
                        sensor.NameEmbedding = embedding;
                    }
                    else
                    {
                        allEmbeddingsFound = false;
                        break;
                    }
                }

                if (allEmbeddingsFound)
                {
                    foreach (var sensor in cached)
                    {
                        if (sensor.PhoneticKeys.Length == 0)
                            sensor.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(sensor.FriendlyName);
                    }

                    _cachedSensors = [.. cached];
                    Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);
                    _logger.LogInformation("Loaded {Count} sensors from Redis cache", cached.Count);
                    return;
                }
            }

            _logger.LogDebug("Refreshing sensor cache from Home Assistant...");

            var allStates = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken).ConfigureAwait(false);

            var sensorEntities = allStates
                .Where(s => s.EntityId.StartsWith("sensor.", StringComparison.OrdinalIgnoreCase)
                         || s.EntityId.StartsWith("binary_sensor.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var newSensors = new List<SensorEntity>();

            foreach (var entity in sensorEntities)
            {
                var friendlyName = entity.Attributes.TryGetValue("friendly_name", out var nameObj)
                    ? nameObj?.ToString() ?? entity.EntityId
                    : entity.EntityId;

                var deviceClass = entity.Attributes.TryGetValue("device_class", out var dcObj)
                    ? dcObj?.ToString()
                    : null;

                var unitOfMeasurement = entity.Attributes.TryGetValue("unit_of_measurement", out var uomObj)
                    ? uomObj?.ToString()
                    : null;

                var stateClass = entity.Attributes.TryGetValue("state_class", out var scObj)
                    ? scObj?.ToString()
                    : null;

                // Resolve area from the shared entity location service
                var areaInfo = _locationService.GetAreaForEntity(entity.EntityId);

                var embedding = await _embeddingService.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false);

                newSensors.Add(new SensorEntity
                {
                    EntityId = entity.EntityId,
                    FriendlyName = friendlyName,
                    NameEmbedding = embedding,
                    PhoneticKeys = StringSimilarity.BuildPhoneticKeys(friendlyName),
                    Area = areaInfo?.Name,
                    DeviceClass = deviceClass,
                    UnitOfMeasurement = unitOfMeasurement,
                    StateClass = stateClass
                });
            }

            _cachedSensors = [.. newSensors];
            Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);

            var ttl = TimeSpan.FromMinutes(_options.CurrentValue.CacheRefreshMinutes);
            var embedTtl = TimeSpan.FromHours(24);
            await _deviceCache.SetCachedSensorsAsync(newSensors, ttl, cancellationToken).ConfigureAwait(false);
            foreach (var sensor in newSensors.Where(s => s.NameEmbedding is not null))
                await _deviceCache.SetEmbeddingAsync($"sensor:{sensor.EntityId}", sensor.NameEmbedding!, embedTtl, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Cached {Count} sensor entities", newSensors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing sensor cache");
        }
        finally
        {
            CacheRefreshDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private async Task EnsureCacheIsCurrentAsync(CancellationToken cancellationToken = default)
    {
        var cacheRefreshInterval = TimeSpan.FromMinutes(_options.CurrentValue.CacheRefreshMinutes);
        if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) > cacheRefreshInterval)
        {
            if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return;
            try
            {
                if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) > cacheRefreshInterval)
                    await RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }
}