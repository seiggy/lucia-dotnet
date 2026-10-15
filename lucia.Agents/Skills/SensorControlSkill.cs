using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
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
public sealed class SensorControlSkill : ILuciaSkill, IOptimizableSkill, ICommandPatternProvider
{
    private const string SensorSearchErrorMessage = "Unable to query sensor data. Please try again.";
    private const string AreaSensorSearchErrorMessage = "Unable to query sensors for that area. Please try again.";
    private const string SensorStateErrorMessage = "Unable to retrieve sensor data. Please try again.";
    private const string BinarySensorStateErrorMessage = "Unable to retrieve binary sensor data. Please try again.";

    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly ILogger<SensorControlSkill> _logger;
    private readonly IDeviceCacheService _deviceCache;
    private readonly IEntityLocationService _locationService;
    private readonly IHybridEntityMatcher _entityMatcher;
    private readonly IOptionsMonitor<SensorControlSkillOptions> _options;
    private readonly SkillEntityIndex<SensorEntity> _sensors;
    private volatile bool _skipDeviceCache;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.SensorControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.SensorControl", "1.0.0");
    private static readonly Counter<long> SensorSearchRequests = Meter.CreateCounter<long>("sensor.search.requests");
    private static readonly Counter<long> SensorSearchSuccess = Meter.CreateCounter<long>("sensor.search.success");
    private static readonly Counter<long> SensorSearchFailures = Meter.CreateCounter<long>("sensor.search.failures");
    private static readonly Histogram<double> SensorSearchDurationMs = Meter.CreateHistogram<double>("sensor.search.duration", "ms");
    private static readonly Histogram<double> CacheRefreshDurationMs = Meter.CreateHistogram<double>("sensor.cache.refresh.duration", "ms");
    private static readonly ToolMetrics LookupMetrics = ToolMetrics.Create(Meter, "sensor.lookup", "sensor lookup");

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
        _logger = logger;
        _deviceCache = deviceCache;
        _locationService = locationService;
        _entityMatcher = entityMatcher;
        _options = options;
        _sensors = new SkillEntityIndex<SensorEntity>(
            embeddingResolver,
            logger,
            "sensor",
            LoadSensorsAsync,
            () => TimeSpan.FromMinutes(options.CurrentValue.CacheRefreshMinutes),
            CacheRefreshDurationMs);
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

        await _sensors.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (_sensors.Embeddings is null)
            return;

        _logger.LogInformation("SensorControlSkill initialized with {SensorCount} sensor entities", _sensors.Items.Length);
    }

    /// <summary>
    /// Re-resolves the embedding generator using the specified provider name.
    /// Called by the owning agent when the embedding configuration changes.
    /// </summary>
    public Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
        => _sensors.UpdateEmbeddingProviderAsync(providerName, cancellationToken);

    /// <summary>Rebuilds the sensors from Home Assistant, skipping the shared device cache, so every name is embedded again.</summary>
    public async Task InvalidateEmbeddingCacheAsync(CancellationToken cancellationToken = default)
    {
        _skipDeviceCache = true;
        try
        {
            await _sensors.ReloadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _skipDeviceCache = false;
        }
    }

    [Description("Find a sensor by name or description using natural language. Works for both regular sensors (temperature, humidity, etc.) and binary sensors (motion, door, etc.).")]
    public async Task<string> FindSensorAsync(
        [Description("Name or description of the sensor (e.g., 'living room temperature', 'front door', 'motion sensor', 'battery level')")] string searchTerm)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.term", searchTerm);
        SensorSearchRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, SensorSearchErrorMessage, async () =>
        {
            await _sensors.EnsureCurrentAsync().ConfigureAwait(false);

            if (_sensors.Items.IsEmpty)
            {
                SensorSearchFailures.Add(1);
                return "No sensors available in the system.";
            }

            var embeddings = _sensors.Embeddings;
            if (embeddings is null)
            {
                SensorSearchFailures.Add(1);
                return "Embedding provider not available for sensor search.";
            }

            var matches = await _entityMatcher.FindMatchesAsync(
                searchTerm,
                (IReadOnlyList<SensorEntity>)_sensors.Items,
                embeddings,
                GetCurrentMatchOptions()).ConfigureAwait(false);

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
                var areaSensors = _sensors.Items.Where(s => matchedEntityIds.Contains(s.EntityId)).ToList();

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
            return $"No sensor found matching '{searchTerm}'. Available sensors: {string.Join(", ", _sensors.Items.Take(5).Select(s => s.FriendlyName))}";
        }, onFailure: () => SensorSearchFailures.Add(1), duration: SensorSearchDurationMs, detail: searchTerm).ConfigureAwait(false);
    }

    [Description("Find all sensors in a specific area/room. Returns both regular sensors and binary sensors.")]
    public async Task<string> FindSensorsByAreaAsync(
        [Description("The area/room name to search (e.g., 'living room', 'garage', 'kitchen')")] string areaName)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);

        return await ToolRunner.RunAsync(_logger, activity, LookupMetrics.ForTool(nameof(FindSensorsByAreaAsync)), AreaSensorSearchErrorMessage, async () =>
        {
            await _sensors.EnsureCurrentAsync().ConfigureAwait(false);

            if (_sensors.Items.IsEmpty)
                return "No sensors available in the system.";

            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                areaName, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);
            var locationEntities = hierarchyResult.ResolvedEntities;

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy == ResolutionStrategy.None || locationEntities.Count == 0)
            {
                var availableAreas = _sensors.Items
                    .Where(s => !string.IsNullOrEmpty(s.Area))
                    .Select(s => s.Area!)
                    .Distinct();
                return $"No area found matching '{areaName}'. {hierarchyResult.ResolutionReason}. Available areas with sensors: {string.Join(", ", availableAreas)}";
            }

            var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaSensors = _sensors.Items.Where(s => matchedEntityIds.Contains(s.EntityId)).ToList();
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
        }, detail: areaName).ConfigureAwait(false);
    }

    [Description("Get the current reading of a specific sensor entity. Use this for regular sensors (temperature, humidity, battery, power, etc.). Sensors are read-only.")]
    public async Task<string> GetSensorStateAsync(
        [Description("The entity ID of the sensor (e.g., 'sensor.living_room_temperature', 'sensor.battery_level')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        return await ToolRunner.RunAsync(_logger, activity, LookupMetrics.ForTool(nameof(GetSensorStateAsync)), SensorStateErrorMessage, async () =>
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Sensor '{entityId}' not found.";

            var device = _sensors.Items.FirstOrDefault(s => s.EntityId == entityId);
            return FormatSensorState(state, device);
        }, detail: entityId).ConfigureAwait(false);
    }

    [Description("Get the current state of a specific binary sensor entity. Binary sensors have on/off states (e.g., motion detected, door open/closed, window open). They are read-only.")]
    public async Task<string> GetBinarySensorStateAsync(
        [Description("The entity ID of the binary sensor (e.g., 'binary_sensor.front_door', 'binary_sensor.living_room_motion')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        return await ToolRunner.RunAsync(_logger, activity, LookupMetrics.ForTool(nameof(GetBinarySensorStateAsync)), BinarySensorStateErrorMessage, async () =>
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Binary sensor '{entityId}' not found.";

            var device = _sensors.Items.FirstOrDefault(s => s.EntityId == entityId);

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
        }, detail: entityId).ConfigureAwait(false);
    }

    [Description("Get all sensors of a specific type in an area. Filter by device class to find specific kinds of sensors (e.g., temperature sensors, motion sensors, battery sensors).")]
    public async Task<string> GetAreaSensorsAsync(
        [Description("The area/room name to search (e.g., 'living room', 'kitchen')")] string areaName,
        [Description("Optional device class filter (e.g., 'temperature', 'humidity', 'motion', 'door', 'window', 'battery', 'power', 'illuminance'). Leave empty to return all sensors in the area.")] string? deviceClass = null)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);
        activity?.SetTag("filter.device_class", deviceClass ?? "none");

        return await ToolRunner.RunAsync(_logger, activity, LookupMetrics.ForTool(nameof(GetAreaSensorsAsync)), AreaSensorSearchErrorMessage, async () =>
        {
            await _sensors.EnsureCurrentAsync().ConfigureAwait(false);

            if (_sensors.Items.IsEmpty)
                return "No sensors available in the system.";

            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                areaName, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy == ResolutionStrategy.None || hierarchyResult.ResolvedEntities.Count == 0)
                return $"No area found matching '{areaName}'. {hierarchyResult.ResolutionReason}";

            var matchedEntityIds = hierarchyResult.ResolvedEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaSensors = _sensors.Items.Where(s => matchedEntityIds.Contains(s.EntityId)).ToList();

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
        }, detail: areaName).ConfigureAwait(false);
    }

    private bool IsTrackedSensorEntity(string entityId)
    {
        var separatorIndex = entityId.IndexOf('.');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var entityDomain = entityId[..separatorIndex];
        return _options.CurrentValue.EntityDomains.Any(domain =>
            entityDomain.Equals(NormalizeDomain(domain), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().TrimEnd('.');
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

    private async Task<IReadOnlyList<SensorEntity>> LoadSensorsAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("RefreshCacheAsync");

        var cached = _skipDeviceCache
            ? null
            : await TryLoadFromDeviceCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogInformation("Loaded {Count} sensors from Redis cache", cached.Count);
            return cached;
        }

        _logger.LogDebug("Refreshing sensor cache from Home Assistant...");

        var allStates = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken).ConfigureAwait(false);

        var sensors = new List<SensorEntity>();
        foreach (var state in allStates.Where(state => IsTrackedSensorEntity(state.EntityId)))
            sensors.Add(await BuildSensorAsync(state, embeddings, cancellationToken).ConfigureAwait(false));

        await StoreInDeviceCacheAsync(sensors, cancellationToken).ConfigureAwait(false);
        return sensors;
    }

    /// <summary>Device data only. Areas come from <see cref="IEntityLocationService"/>. Returns null unless every sensor has its embedding.</summary>
    private async Task<List<SensorEntity>?> TryLoadFromDeviceCacheAsync(CancellationToken cancellationToken)
    {
        var cached = await _deviceCache.GetCachedSensorsAsync(cancellationToken).ConfigureAwait(false);
        if (cached is null)
            return null;

        foreach (var sensor in cached)
        {
            var embedding = await _deviceCache.GetEmbeddingAsync($"sensor:{sensor.EntityId}", cancellationToken).ConfigureAwait(false);
            if (embedding is null)
                return null;

            sensor.NameEmbedding = embedding;
        }

        foreach (var sensor in cached.Where(s => s.PhoneticKeys.Length == 0))
            sensor.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(sensor.FriendlyName);

        return cached;
    }

    private async Task<SensorEntity> BuildSensorAsync(
        HomeAssistantState entity,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        CancellationToken cancellationToken)
    {
        var friendlyName = entity.Attributes.TryGetValue("friendly_name", out var nameObj)
            ? nameObj?.ToString() ?? entity.EntityId
            : entity.EntityId;

        return new SensorEntity
        {
            EntityId = entity.EntityId,
            FriendlyName = friendlyName,
            NameEmbedding = await embeddings.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false),
            PhoneticKeys = StringSimilarity.BuildPhoneticKeys(friendlyName),
            Area = _locationService.GetAreaForEntity(entity.EntityId)?.Name,
            DeviceClass = entity.Attributes.TryGetValue("device_class", out var dcObj) ? dcObj?.ToString() : null,
            UnitOfMeasurement = entity.Attributes.TryGetValue("unit_of_measurement", out var uomObj) ? uomObj?.ToString() : null,
            StateClass = entity.Attributes.TryGetValue("state_class", out var scObj) ? scObj?.ToString() : null
        };
    }

    /// <summary>A failed write must not discard sensors that were just loaded, so it only logs.</summary>
    private async Task StoreInDeviceCacheAsync(List<SensorEntity> sensors, CancellationToken cancellationToken)
    {
        try
        {
            var ttl = TimeSpan.FromMinutes(_options.CurrentValue.CacheRefreshMinutes);
            await _deviceCache.SetCachedSensorsAsync(sensors, ttl, cancellationToken).ConfigureAwait(false);
            foreach (var sensor in sensors.Where(s => s.NameEmbedding is not null))
                await _deviceCache.SetEmbeddingAsync($"sensor:{sensor.EntityId}", sensor.NameEmbedding!, TimeSpan.FromHours(24), cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Cached {Count} sensor entities", sensors.Count);
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogError(ex, "Error caching sensor entities");
        }
    }
}