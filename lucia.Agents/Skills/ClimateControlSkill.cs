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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for controlling HVAC / climate entities in Home Assistant
/// </summary>
public sealed class ClimateControlSkill : IAgentSkill, IOptimizableSkill, ICommandPatternProvider
{
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly ILogger<ClimateControlSkill> _logger;
    private readonly IDeviceCacheService _deviceCache;
    private readonly IEntityLocationService _locationService;
    private readonly IHybridEntityMatcher _entityMatcher;
    private readonly IOptionsMonitor<ClimateControlSkillOptions> _options;
    private readonly int _comfortAdjustmentF;
    private readonly SkillEntityIndex<ClimateEntity> _devices;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.ClimateControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.ClimateControl", "1.0.0");
    private static readonly Counter<long> ClimateSearchRequests = Meter.CreateCounter<long>("climate.search.requests");
    private static readonly Counter<long> ClimateSearchSuccess = Meter.CreateCounter<long>("climate.search.success");
    private static readonly Counter<long> ClimateSearchFailures = Meter.CreateCounter<long>("climate.search.failures");
    private static readonly Histogram<double> ClimateSearchDurationMs = Meter.CreateHistogram<double>("climate.search.duration", "ms");
    private static readonly Counter<long> ClimateControlRequests = Meter.CreateCounter<long>("climate.control.requests");
    private static readonly Counter<long> ClimateControlFailures = Meter.CreateCounter<long>("climate.control.failures");
    private static readonly Histogram<double> ClimateControlDurationMs = Meter.CreateHistogram<double>("climate.control.duration", "ms");
    private static readonly Histogram<double> CacheRefreshDurationMs = Meter.CreateHistogram<double>("climate.cache.refresh.duration", "ms");

    public ClimateControlSkill(
        IHomeAssistantClient homeAssistantClient,
        IEmbeddingProviderResolver embeddingResolver,
        ILogger<ClimateControlSkill> logger,
        IDeviceCacheService deviceCache,
        IEntityLocationService locationService,
        IHybridEntityMatcher entityMatcher,
        IOptionsMonitor<ClimateControlSkillOptions> options,
        IConfiguration configuration)
    {
        _homeAssistantClient = homeAssistantClient;
        _logger = logger;
        _deviceCache = deviceCache;
        _locationService = locationService;
        _entityMatcher = entityMatcher;
        _options = options;
        _comfortAdjustmentF = configuration.GetValue("ClimateAgent:ComfortAdjustmentF", 3);
        _devices = new SkillEntityIndex<ClimateEntity>(
            embeddingResolver,
            logger,
            "climate",
            LoadDevicesAsync,
            () => TimeSpan.FromMinutes(options.CurrentValue.CacheRefreshMinutes),
            CacheRefreshDurationMs);
    }

    public IList<AITool> GetTools()
    {
        return [
            AIFunctionFactory.Create(FindClimateDeviceAsync),
            AIFunctionFactory.Create(FindClimateDevicesByAreaAsync),
            AIFunctionFactory.Create(GetClimateStateAsync),
            AIFunctionFactory.Create(GetAreaClimateSensorsAsync),
            AIFunctionFactory.Create(GetSensorStateAsync),
            AIFunctionFactory.Create(SetClimateTemperatureAsync),
            AIFunctionFactory.Create(SetClimateHvacModeAsync),
            AIFunctionFactory.Create(SetClimateFanModeAsync),
            AIFunctionFactory.Create(SetClimateHumidityAsync),
            AIFunctionFactory.Create(SetClimatePresetModeAsync),
            AIFunctionFactory.Create(SetClimateSwingModeAsync),
            AIFunctionFactory.Create(GetComfortAdjustment)
        ];
    }

    public IReadOnlyList<CommandPatternDefinition> GetCommandPatterns() =>
    [
        new()
        {
            Id = "climate-set-temp",
            SkillId = "ClimateControlSkill",
            Action = "set_temperature",
            Templates =
            [
                "{action:set} [the] {entity} [temperature] to {value} [degrees]",
                "{action:set} [the] thermostat [in] [the] {area} to {value}",
            ],
        },
        new()
        {
            Id = "climate-comfort",
            SkillId = "ClimateControlSkill",
            Action = "adjust",
            Templates =
            [
                "make [it] {action:warmer|cooler|hotter|colder} [in] [the] {area}",
            ],
            MinConfidence = 0.85f,
        },
    ];

    // ── IOptimizableSkill ─────────────────────────────────────────

    /// <inheritdoc/>
    public string SkillDisplayName => "Climate Control";

    /// <inheritdoc/>
    public string SkillId => "climate-control";

    /// <inheritdoc/>
    public string AgentId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<string> SearchToolNames { get; } = ["FindClimateDevice", "FindClimateDevicesByArea"];

    /// <inheritdoc/>
    public string ConfigSectionName => ClimateControlSkillOptions.SectionName;

    /// <inheritdoc/>
    public IReadOnlyList<string> EntityDomains => _options.CurrentValue.EntityDomains;

    /// <inheritdoc/>
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
        _logger.LogInformation("Initializing ClimateControlSkill and caching climate entities...");

        await _devices.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (_devices.Embeddings is null)
            return;

        _logger.LogInformation("ClimateControlSkill initialized with {DeviceCount} climate entities", _devices.Items.Length);
    }

    /// <summary>
    /// Re-resolves the embedding generator using the specified provider name.
    /// Called by the owning agent when the embedding configuration changes.
    /// </summary>
    public Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
        => _devices.UpdateEmbeddingProviderAsync(providerName, cancellationToken);

    [Description("Get the configured comfort adjustment in degrees Fahrenheit used for relative temperature changes like 'I'm cold' or 'I'm hot'")]
    public string GetComfortAdjustment()
    {
        return $"The configured comfort adjustment is {_comfortAdjustmentF}°F. When the user says they are cold, increase the target temperature by {_comfortAdjustmentF}°F. When the user says they are hot, decrease it by {_comfortAdjustmentF}°F.";
    }

    [Description("Find a climate/HVAC device by name or description using natural language")]
    public async Task<string> FindClimateDeviceAsync(
        [Description("Name or description of the climate device (e.g., 'living room thermostat', 'downstairs HVAC', 'bedroom climate')")] string searchTerm)
    {
        await _devices.EnsureCurrentAsync().ConfigureAwait(false);

        if (_devices.Items.IsEmpty)
        {
            ClimateSearchFailures.Add(1);
            return "No climate devices available in the system.";
        }

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.term", searchTerm);
        ClimateSearchRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, "Unable to search for climate devices. Please try again.", async () =>
        {
            var embeddings = _devices.Embeddings;
            if (embeddings is null)
            {
                ClimateSearchFailures.Add(1);
                return "Embedding provider not available for climate search.";
            }

            var matches = await _entityMatcher.FindMatchesAsync(
                searchTerm,
                (IReadOnlyList<ClimateEntity>)_devices.Items,
                embeddings,
                GetCurrentMatchOptions()).ConfigureAwait(false);

            if (matches.Count > 0)
            {
                activity?.SetTag("match.type", "device");
                activity?.SetTag("match.count", matches.Count);
                activity?.SetTag("match.top_similarity", matches[0].HybridScore);

                var sb = new StringBuilder();
                if (matches.Count == 1)
                    sb.Append("Found climate device: ");
                else
                    sb.AppendLine($"Found {matches.Count} matching climate device(s):");

                foreach (var match in matches)
                {
                    var device = match.Entity;
                    var state = await _homeAssistantClient.GetEntityStateAsync(device.EntityId).ConfigureAwait(false);
                    if (state is null) continue;

                    var stateInfo = FormatClimateState(state, device);

                    if (matches.Count == 1)
                        sb.Append($"{device.FriendlyName} (Entity ID: {device.EntityId}), {stateInfo}");
                    else
                        sb.AppendLine($"- {device.FriendlyName} (Entity ID: {device.EntityId}), {stateInfo}");
                }

                ClimateSearchSuccess.Add(1);
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
                var areaDevices = _devices.Items.Where(d => matchedEntityIds.Contains(d.EntityId)).ToList();

                if (areaDevices.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Found {areaDevices.Count} climate device(s) matching '{searchTerm}':");

                    foreach (var device in areaDevices)
                    {
                        var state = await _homeAssistantClient.GetEntityStateAsync(device.EntityId).ConfigureAwait(false);
                        if (state is null) continue;
                        sb.AppendLine($"- {device.FriendlyName} (Entity ID: {device.EntityId}), {FormatClimateState(state, device)}");
                    }

                    ClimateSearchSuccess.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return sb.ToString().TrimEnd();
                }
            }

            ClimateSearchFailures.Add(1);
            return $"No climate device or area found matching '{searchTerm}'. Available devices: {string.Join(", ", _devices.Items.Take(5).Select(d => d.FriendlyName))}";
        }, onFailure: () => ClimateSearchFailures.Add(1), duration: ClimateSearchDurationMs, detail: searchTerm).ConfigureAwait(false);
    }

    [Description("Find all climate/HVAC devices in a specific area")]
    public async Task<string> FindClimateDevicesByAreaAsync(
        [Description("The area/room name to search (e.g., 'living room', 'upstairs', 'basement')")] string areaName)
    {
        await _devices.EnsureCurrentAsync().ConfigureAwait(false);

        if (_devices.Items.IsEmpty)
            return "No climate devices available in the system.";

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);

        return await ToolRunner.RunAsync(_logger, "Unable to search for climate devices in that area. Please try again.", async () =>
        {
            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                areaName, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);
            var locationEntities = hierarchyResult.ResolvedEntities;

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy == ResolutionStrategy.None || locationEntities.Count == 0)
            {
                var availableAreas = _devices.Items
                    .Where(d => !string.IsNullOrEmpty(d.Area))
                    .Select(d => d.Area!)
                    .Distinct();
                return $"No area found matching '{areaName}'. {hierarchyResult.ResolutionReason}. Available areas with climate devices: {string.Join(", ", availableAreas)}";
            }

            var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaDevices = _devices.Items.Where(d => matchedEntityIds.Contains(d.EntityId)).ToList();
            if (!areaDevices.Any())
                return $"No climate devices found in the matched location for '{areaName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {areaDevices.Count} climate device(s) matching '{areaName}':");

            foreach (var device in areaDevices)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(device.EntityId).ConfigureAwait(false);
                if (state is null) continue;
                sb.AppendLine($"- {device.FriendlyName} (Entity ID: {device.EntityId}), {FormatClimateState(state, device)}");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }, detail: areaName).ConfigureAwait(false);
    }

    [Description("Get the current state of a specific climate/HVAC device including temperature, mode, and target temperature")]
    public async Task<string> GetClimateStateAsync(
        [Description("The entity ID of the climate device (e.g., 'climate.living_room')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        return await ToolRunner.RunAsync(_logger, $"Unable to get the state of '{entityId}'. Please try again.", async () =>
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Climate device '{entityId}' not found.";

            var device = _devices.Items.FirstOrDefault(d => d.EntityId == entityId);
            return FormatClimateState(state, device);
        }).ConfigureAwait(false);
    }

    [Description("Find temperature and humidity sensors in a specific area/room. Returns sensor entity IDs and their current readings.")]
    public async Task<string> GetAreaClimateSensorsAsync(
        [Description("The area/room name to search (e.g., 'living room', 'upstairs', 'bedroom')")] string areaName)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);

        return await ToolRunner.RunAsync(_logger, "Unable to search for climate sensors in that area. Please try again.", async () =>
        {
            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                areaName, GetCurrentMatchOptions(), (IReadOnlyList<string>)["sensor"], CancellationToken.None).ConfigureAwait(false);

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            var climateSensors = hierarchyResult.ResolvedEntities
                .Where(e => e.EntityId.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                         || e.EntityId.Contains("humidity", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (climateSensors.Count == 0)
                return $"No temperature or humidity sensors found in '{areaName}'. {hierarchyResult.ResolutionReason}";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {climateSensors.Count} climate sensor(s) in '{areaName}':");

            foreach (var sensor in climateSensors)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(sensor.EntityId).ConfigureAwait(false);
                if (state is null) continue;

                var unit = state.Attributes.TryGetValue("unit_of_measurement", out var u) ? u?.ToString() : "";
                var friendlyName = state.Attributes.TryGetValue("friendly_name", out var fn) ? fn?.ToString() : sensor.EntityId;
                sb.AppendLine($"- {friendlyName}: {state.State}{unit} (Entity ID: {sensor.EntityId})");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }, detail: areaName).ConfigureAwait(false);
    }

    [Description("Get the current reading of a specific sensor entity (e.g., temperature or humidity sensor). Sensors are read-only.")]
    public async Task<string> GetSensorStateAsync(
        [Description("The entity ID of the sensor (e.g., 'sensor.living_room_temperature', 'sensor.bedroom_humidity')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        return await ToolRunner.RunAsync(_logger, $"Unable to get the state of '{entityId}'. Please try again.", async () =>
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Sensor '{entityId}' not found.";

            var unit = state.Attributes.TryGetValue("unit_of_measurement", out var u) ? u?.ToString() : "";
            var friendlyName = state.Attributes.TryGetValue("friendly_name", out var fn) ? fn?.ToString() : entityId;
            var deviceClass = state.Attributes.TryGetValue("device_class", out var dc) ? dc?.ToString() : null;

            var result = $"Sensor '{friendlyName}': {state.State}{unit}";
            if (deviceClass is not null)
                result += $" (type: {deviceClass})";

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }).ConfigureAwait(false);
    }

    [Description("Set the target temperature for a climate/HVAC device")]
    public Task<string> SetClimateTemperatureAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The target temperature in the device's native unit")] double temperature)
        => CallClimateServiceAsync(
            "set_temperature", entityId, "temperature", temperature, "temperature",
            $"Set target temperature for '{entityId}' to {temperature}°.");

    [Description("Set the HVAC mode for a climate device (e.g., heat, cool, auto, off, heat_cool, dry, fan_only)")]
    public Task<string> SetClimateHvacModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The HVAC mode to set (heat, cool, auto, off, heat_cool, dry, fan_only)")] string hvacMode)
        => CallClimateServiceAsync(
            "set_hvac_mode", entityId, "hvac_mode", hvacMode, "HVAC mode",
            $"Set HVAC mode for '{entityId}' to '{hvacMode}'.");

    [Description("Set the fan mode on a climate/HVAC device (e.g., auto, low, medium, high).")]
    public Task<string> SetClimateFanModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The fan mode to set (e.g., auto, low, medium, high, on)")] string fanMode)
        => CallClimateServiceAsync(
            "set_fan_mode", entityId, "fan_mode", fanMode, "fan mode",
            $"Set fan mode for '{entityId}' to '{fanMode}'.");

    [Description("Set the target humidity on a climate device that supports humidity control.")]
    public Task<string> SetClimateHumidityAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The target humidity percentage (30-99)")] int humidity)
        => CallClimateServiceAsync(
            "set_humidity", entityId, "humidity", humidity, "humidity",
            $"Set target humidity for '{entityId}' to {humidity}%.");

    [Description("Set the preset mode on a climate device (e.g., none, sleep, eco, away).")]
    public Task<string> SetClimatePresetModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The preset mode to set (e.g., 'none', 'sleep', 'eco', 'away')")] string presetMode)
        => CallClimateServiceAsync(
            "set_preset_mode", entityId, "preset_mode", presetMode, "preset mode",
            $"Set preset mode for '{entityId}' to '{presetMode}'.");

    [Description("Set the swing mode on a climate device (e.g., on, off, or a specific position).")]
    public Task<string> SetClimateSwingModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The swing mode to set (e.g., 'on', 'off', 'Auto', 'Position 1')")] string swingMode)
        => CallClimateServiceAsync(
            "set_swing_mode", entityId, "swing_mode", swingMode, "swing mode",
            $"Set swing mode for '{entityId}' to '{swingMode}'.");

    /// <summary>
    /// Calls one <c>climate</c> service with a single data field. Every control tool differs only in the
    /// service name, the field, and the wording.
    /// </summary>
    private async Task<string> CallClimateServiceAsync(
        string service,
        string entityId,
        string dataKey,
        object value,
        string what,
        string successMessage)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag(dataKey, value);
        ClimateControlRequests.Add(1);

        return await ToolRunner.RunAsync(
            _logger,
            $"Unable to set {what} for '{entityId}'. Please try again.",
            async () =>
            {
                var request = new ServiceCallRequest
                {
                    EntityId = entityId,
                    [dataKey] = value
                };

                await _homeAssistantClient.CallServiceAsync("climate", service, request: request).ConfigureAwait(false);

                _logger.LogInformation("Set {What} for {EntityId} to {Value}", what, entityId, value);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return successMessage;
            },
            onFailure: () => ClimateControlFailures.Add(1),
            duration: ClimateControlDurationMs).ConfigureAwait(false);
    }

    private static string FormatClimateState(HomeAssistantState state, ClimateEntity? device)
    {
        var sb = new StringBuilder();
        sb.Append($"State: {state.State}");

        if (state.Attributes.TryGetValue("current_temperature", out var currentTempObj))
            sb.Append($", Current Temperature: {currentTempObj}°");

        if (state.Attributes.TryGetValue("temperature", out var targetTempObj))
            sb.Append($", Target Temperature: {targetTempObj}°");

        if (state.Attributes.TryGetValue("target_temp_high", out var highObj))
            sb.Append($", Target High: {highObj}°");

        if (state.Attributes.TryGetValue("target_temp_low", out var lowObj))
            sb.Append($", Target Low: {lowObj}°");

        if (state.Attributes.TryGetValue("current_humidity", out var curHumObj))
            sb.Append($", Current Humidity: {curHumObj}%");

        if (state.Attributes.TryGetValue("humidity", out var targetHumObj))
            sb.Append($", Target Humidity: {targetHumObj}%");

        if (state.Attributes.TryGetValue("hvac_action", out var actionObj))
            sb.Append($", Action: {actionObj}");

        if (state.Attributes.TryGetValue("fan_mode", out var fanModeObj))
            sb.Append($", Fan Mode: {fanModeObj}");

        if (state.Attributes.TryGetValue("swing_mode", out var swingModeObj))
            sb.Append($", Swing Mode: {swingModeObj}");

        if (state.Attributes.TryGetValue("preset_mode", out var presetModeObj))
            sb.Append($", Preset Mode: {presetModeObj}");

        if (device?.HvacModes.Count > 0)
            sb.Append($", Available HVAC Modes: [{string.Join(", ", device.HvacModes)}]");

        if (device?.FanModes.Count > 0)
            sb.Append($", Available Fan Modes: [{string.Join(", ", device.FanModes)}]");

        if (device?.SwingModes.Count > 0)
            sb.Append($", Available Swing Modes: [{string.Join(", ", device.SwingModes)}]");

        if (device?.PresetModes.Count > 0)
            sb.Append($", Available Preset Modes: [{string.Join(", ", device.PresetModes)}]");

        if (device is not null)
            sb.Append($", Temp Range: {device.MinTemp}°-{device.MaxTemp}°");

        return sb.ToString();
    }

    private async Task<IReadOnlyList<ClimateEntity>> LoadDevicesAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("RefreshCacheAsync");

        var cached = await TryLoadFromDeviceCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogInformation("Loaded {Count} climate devices from Redis cache", cached.Count);
            return cached;
        }

        _logger.LogDebug("Refreshing climate cache from Home Assistant...");

        var allStates = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken).ConfigureAwait(false);

        var devices = new List<ClimateEntity>();
        foreach (var state in allStates.Where(s => s.EntityId.StartsWith("climate.")))
            devices.Add(await BuildDeviceAsync(state, embeddings, cancellationToken).ConfigureAwait(false));

        await StoreInDeviceCacheAsync(devices, cancellationToken).ConfigureAwait(false);
        return devices;
    }

    /// <summary>Device data only. Areas come from <see cref="IEntityLocationService"/>. Returns null unless every device has its embedding.</summary>
    private async Task<List<ClimateEntity>?> TryLoadFromDeviceCacheAsync(CancellationToken cancellationToken)
    {
        var cached = await _deviceCache.GetCachedClimateDevicesAsync(cancellationToken).ConfigureAwait(false);
        if (cached is null)
            return null;

        foreach (var device in cached)
        {
            var embedding = await _deviceCache.GetEmbeddingAsync($"climate:{device.EntityId}", cancellationToken).ConfigureAwait(false);
            if (embedding is null)
                return null;

            device.NameEmbedding = embedding;
        }

        foreach (var device in cached.Where(d => d.PhoneticKeys.Length == 0))
            device.PhoneticKeys = StringSimilarity.BuildPhoneticKeys(device.FriendlyName);

        return cached;
    }

    private async Task<ClimateEntity> BuildDeviceAsync(
        HomeAssistantState entity,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        CancellationToken cancellationToken)
    {
        var friendlyName = entity.Attributes.TryGetValue("friendly_name", out var nameObj)
            ? nameObj?.ToString() ?? entity.EntityId
            : entity.EntityId;

        double? minTemp = null, maxTemp = null;
        if (entity.Attributes.TryGetValue("min_temp", out var minObj) && double.TryParse(minObj?.ToString(), out var min))
            minTemp = min;
        if (entity.Attributes.TryGetValue("max_temp", out var maxObj) && double.TryParse(maxObj?.ToString(), out var max))
            maxTemp = max;

        double? minHumidity = null, maxHumidity = null;
        if (entity.Attributes.TryGetValue("min_humidity", out var minHumObj) && double.TryParse(minHumObj?.ToString(), out var minH))
            minHumidity = minH;
        if (entity.Attributes.TryGetValue("max_humidity", out var maxHumObj) && double.TryParse(maxHumObj?.ToString(), out var maxH))
            maxHumidity = maxH;

        var supportedFeatures = 0;
        if (entity.Attributes.TryGetValue("supported_features", out var featObj) && int.TryParse(featObj?.ToString(), out var feat))
            supportedFeatures = feat;

        return new ClimateEntity
        {
            EntityId = entity.EntityId,
            FriendlyName = friendlyName,
            NameEmbedding = await embeddings.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false),
            PhoneticKeys = StringSimilarity.BuildPhoneticKeys(friendlyName),
            Area = _locationService.GetAreaForEntity(entity.EntityId)?.Name,
            HvacModes = AttributeParser.ParseStringList(entity.Attributes, "hvac_modes"),
            FanModes = AttributeParser.ParseStringList(entity.Attributes, "fan_modes"),
            SwingModes = AttributeParser.ParseStringList(entity.Attributes, "swing_modes"),
            PresetModes = AttributeParser.ParseStringList(entity.Attributes, "preset_modes"),
            MinTemp = minTemp,
            MaxTemp = maxTemp,
            MinHumidity = minHumidity,
            MaxHumidity = maxHumidity,
            SupportedFeatures = supportedFeatures
        };
    }

    /// <summary>A failed write must not discard devices that were just loaded, so it only logs.</summary>
    private async Task StoreInDeviceCacheAsync(List<ClimateEntity> devices, CancellationToken cancellationToken)
    {
        try
        {
            await _deviceCache.SetCachedClimateDevicesAsync(devices, TimeSpan.FromMinutes(30), cancellationToken).ConfigureAwait(false);
            foreach (var device in devices.Where(d => d.NameEmbedding is not null))
                await _deviceCache.SetEmbeddingAsync($"climate:{device.EntityId}", device.NameEmbedding!, TimeSpan.FromHours(24), cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Cached {Count} climate devices", devices.Count);
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogError(ex, "Error caching climate devices");
        }
    }
}
