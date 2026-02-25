using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for controlling HVAC / climate entities in Home Assistant
/// </summary>
public sealed class ClimateControlSkill : IAgentSkill
{
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingService;
    private readonly ILogger<ClimateControlSkill> _logger;
    private readonly IDeviceCacheService _deviceCache;
    private readonly IEntityLocationService _locationService;
    private readonly IEmbeddingSimilarityService _similarity;
    private readonly int _comfortAdjustmentF;
    private ImmutableArray<ClimateEntity> _cachedDevices = [];
    private long _lastCacheUpdateTicks = DateTime.MinValue.Ticks;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(30);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

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
        IEmbeddingSimilarityService similarity,
        IConfiguration configuration)
    {
        _homeAssistantClient = homeAssistantClient;
        _embeddingResolver = embeddingResolver;
        _logger = logger;
        _deviceCache = deviceCache;
        _locationService = locationService;
        _similarity = similarity;
        _comfortAdjustmentF = configuration.GetValue("ClimateAgent:ComfortAdjustmentF", 3);
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing ClimateControlSkill and caching climate entities...");

        _embeddingService = await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
        if (_embeddingService is null)
        {
            _logger.LogWarning("No embedding provider configured — climate semantic search will not be available.");
            return;
        }

        await RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("ClimateControlSkill initialized with {DeviceCount} climate entities", _cachedDevices.Length);
    }

    /// <summary>
    /// Re-resolves the embedding generator using the specified provider name.
    /// Called by the owning agent when the embedding configuration changes.
    /// </summary>
    public async Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
    {
        _embeddingService = await _embeddingResolver.ResolveAsync(providerName, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("ClimateControlSkill: embedding provider updated to '{Provider}'", providerName ?? "system-default");
    }

    [Description("Get the configured comfort adjustment in degrees Fahrenheit used for relative temperature changes like 'I'm cold' or 'I'm hot'")]
    public string GetComfortAdjustment()
    {
        return $"The configured comfort adjustment is {_comfortAdjustmentF}°F. When the user says they are cold, increase the target temperature by {_comfortAdjustmentF}°F. When the user says they are hot, decrease it by {_comfortAdjustmentF}°F.";
    }

    [Description("Find a climate/HVAC device by name or description using natural language")]
    public async Task<string> FindClimateDeviceAsync(
        [Description("Name or description of the climate device (e.g., 'living room thermostat', 'downstairs HVAC', 'bedroom climate')")] string searchTerm)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        if (_cachedDevices.IsEmpty)
        {
            ClimateSearchFailures.Add(1);
            return "No climate devices available in the system.";
        }

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.term", searchTerm);
        var start = Stopwatch.GetTimestamp();
        ClimateSearchRequests.Add(1);

        try
        {
            var searchEmbedding = await _embeddingService!.GenerateAsync(searchTerm, cancellationToken: CancellationToken.None).ConfigureAwait(false);

            var deviceMatches = _cachedDevices
                .Select(device => new { Device = device, Similarity = _similarity.ComputeSimilarity(searchEmbedding, device.NameEmbedding) })
                .ToList();

            const double similarityThreshold = 0.6;

            var matchingDevices = deviceMatches
                .Where(x => x.Similarity >= similarityThreshold)
                .OrderByDescending(x => x.Similarity)
                .ToList();

            if (matchingDevices.Count > 0)
            {
                activity?.SetTag("match.type", "device");
                activity?.SetTag("match.count", matchingDevices.Count);

                var sb = new StringBuilder();
                if (matchingDevices.Count == 1)
                    sb.Append("Found climate device: ");
                else
                    sb.AppendLine($"Found {matchingDevices.Count} matching climate device(s):");

                foreach (var match in matchingDevices)
                {
                    var device = match.Device;
                    var state = await _homeAssistantClient.GetEntityStateAsync(device.EntityId).ConfigureAwait(false);
                    if (state is null) continue;

                    var stateInfo = FormatClimateState(state, device);

                    if (matchingDevices.Count == 1)
                        sb.Append($"{device.FriendlyName} (Entity ID: {device.EntityId}), {stateInfo}");
                    else
                        sb.AppendLine($"- {device.FriendlyName} (Entity ID: {device.EntityId}), {stateInfo}");
                }

                ClimateSearchSuccess.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return matchingDevices.Count == 1 ? sb.ToString() : sb.ToString().TrimEnd();
            }

            // Fallback: use location service for area-based search
            var locationEntities = await _locationService.FindEntitiesByLocationAsync(
                searchTerm, (IReadOnlyList<string>)["climate"], CancellationToken.None).ConfigureAwait(false);

            if (locationEntities.Count > 0)
            {
                var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var areaDevices = _cachedDevices.Where(d => matchedEntityIds.Contains(d.EntityId)).ToList();

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
            return $"No climate device or area found matching '{searchTerm}'. Available devices: {string.Join(", ", _cachedDevices.Take(5).Select(d => d.FriendlyName))}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding climate device for search term: {SearchTerm}", searchTerm);
            ClimateSearchFailures.Add(1);
            return $"Error searching for climate device: {ex.Message}";
        }
        finally
        {
            ClimateSearchDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Find all climate/HVAC devices in a specific area")]
    public async Task<string> FindClimateDevicesByAreaAsync(
        [Description("The area/room name to search (e.g., 'living room', 'upstairs', 'basement')")] string areaName)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        if (_cachedDevices.IsEmpty)
            return "No climate devices available in the system.";

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);

        try
        {
            var locationEntities = await _locationService.FindEntitiesByLocationAsync(
                areaName, (IReadOnlyList<string>)["climate"], CancellationToken.None).ConfigureAwait(false);

            if (locationEntities.Count == 0)
            {
                var availableAreas = _cachedDevices
                    .Where(d => !string.IsNullOrEmpty(d.Area))
                    .Select(d => d.Area!)
                    .Distinct();
                return $"No area found matching '{areaName}'. Available areas with climate devices: {string.Join(", ", availableAreas)}";
            }

            var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaDevices = _cachedDevices.Where(d => matchedEntityIds.Contains(d.EntityId)).ToList();
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding climate devices in area: {AreaName}", areaName);
            return $"Error searching for climate devices in area: {ex.Message}";
        }
    }

    [Description("Get the current state of a specific climate/HVAC device including temperature, mode, and target temperature")]
    public async Task<string> GetClimateStateAsync(
        [Description("The entity ID of the climate device (e.g., 'climate.living_room')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        try
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Climate device '{entityId}' not found.";

            var device = _cachedDevices.FirstOrDefault(d => d.EntityId == entityId);
            return FormatClimateState(state, device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting climate state for {EntityId}", entityId);
            return $"Error getting state for '{entityId}': {ex.Message}";
        }
    }

    [Description("Find temperature and humidity sensors in a specific area/room. Returns sensor entity IDs and their current readings.")]
    public async Task<string> GetAreaClimateSensorsAsync(
        [Description("The area/room name to search (e.g., 'living room', 'upstairs', 'bedroom')")] string areaName)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", areaName);

        try
        {
            var locationEntities = await _locationService.FindEntitiesByLocationAsync(
                areaName, (IReadOnlyList<string>)["sensor"], CancellationToken.None).ConfigureAwait(false);

            var climateSensors = locationEntities
                .Where(e => e.EntityId.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                         || e.EntityId.Contains("humidity", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (climateSensors.Count == 0)
                return $"No temperature or humidity sensors found in '{areaName}'.";

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding climate sensors in area: {AreaName}", areaName);
            return $"Error searching for climate sensors in area: {ex.Message}";
        }
    }

    [Description("Get the current reading of a specific sensor entity (e.g., temperature or humidity sensor). Sensors are read-only.")]
    public async Task<string> GetSensorStateAsync(
        [Description("The entity ID of the sensor (e.g., 'sensor.living_room_temperature', 'sensor.bedroom_humidity')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);

        try
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sensor state for {EntityId}", entityId);
            return $"Error getting state for '{entityId}': {ex.Message}";
        }
    }

    [Description("Set the target temperature for a climate/HVAC device")]
    public async Task<string> SetClimateTemperatureAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The target temperature in the device's native unit")] double temperature)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("temperature", temperature);
        var start = Stopwatch.GetTimestamp();
        ClimateControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["temperature"] = temperature
            };

            await _homeAssistantClient.CallServiceAsync("climate", "set_temperature", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set temperature for {EntityId} to {Temperature}", entityId, temperature);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set target temperature for '{entityId}' to {temperature}°.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting temperature for {EntityId}", entityId);
            ClimateControlFailures.Add(1);
            return $"Failed to set temperature for '{entityId}': {ex.Message}";
        }
        finally
        {
            ClimateControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set the HVAC mode for a climate device (e.g., heat, cool, auto, off, heat_cool, dry, fan_only)")]
    public async Task<string> SetClimateHvacModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The HVAC mode to set (heat, cool, auto, off, heat_cool, dry, fan_only)")] string hvacMode)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("hvac_mode", hvacMode);
        var start = Stopwatch.GetTimestamp();
        ClimateControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["hvac_mode"] = hvacMode
            };

            await _homeAssistantClient.CallServiceAsync("climate", "set_hvac_mode", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set HVAC mode for {EntityId} to {HvacMode}", entityId, hvacMode);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set HVAC mode for '{entityId}' to '{hvacMode}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting HVAC mode for {EntityId}", entityId);
            ClimateControlFailures.Add(1);
            return $"Failed to set HVAC mode for '{entityId}': {ex.Message}";
        }
        finally
        {
            ClimateControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set the fan mode on a climate/HVAC device (e.g., auto, low, medium, high).")]
    public async Task<string> SetClimateFanModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The fan mode to set (e.g., auto, low, medium, high, on)")] string fanMode)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("fan_mode", fanMode);
        var start = Stopwatch.GetTimestamp();
        ClimateControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["fan_mode"] = fanMode
            };

            await _homeAssistantClient.CallServiceAsync("climate", "set_fan_mode", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan mode for {EntityId} to {FanMode}", entityId, fanMode);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set fan mode for '{entityId}' to '{fanMode}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting fan mode for {EntityId}", entityId);
            ClimateControlFailures.Add(1);
            return $"Failed to set fan mode for '{entityId}': {ex.Message}";
        }
        finally
        {
            ClimateControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set the target humidity on a climate device that supports humidity control.")]
    public async Task<string> SetClimateHumidityAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The target humidity percentage (30-99)")] int humidity)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("humidity", humidity);
        var start = Stopwatch.GetTimestamp();
        ClimateControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["humidity"] = humidity
            };

            await _homeAssistantClient.CallServiceAsync("climate", "set_humidity", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set humidity for {EntityId} to {Humidity}%", entityId, humidity);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set target humidity for '{entityId}' to {humidity}%.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting humidity for {EntityId}", entityId);
            ClimateControlFailures.Add(1);
            return $"Failed to set humidity for '{entityId}': {ex.Message}";
        }
        finally
        {
            ClimateControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set the preset mode on a climate device (e.g., none, sleep, eco, away).")]
    public async Task<string> SetClimatePresetModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The preset mode to set (e.g., 'none', 'sleep', 'eco', 'away')")] string presetMode)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("preset_mode", presetMode);
        var start = Stopwatch.GetTimestamp();
        ClimateControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["preset_mode"] = presetMode
            };

            await _homeAssistantClient.CallServiceAsync("climate", "set_preset_mode", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set preset mode for {EntityId} to {PresetMode}", entityId, presetMode);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set preset mode for '{entityId}' to '{presetMode}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting preset mode for {EntityId}", entityId);
            ClimateControlFailures.Add(1);
            return $"Failed to set preset mode for '{entityId}': {ex.Message}";
        }
        finally
        {
            ClimateControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set the swing mode on a climate device (e.g., on, off, or a specific position).")]
    public async Task<string> SetClimateSwingModeAsync(
        [Description("The entity ID of the climate device")] string entityId,
        [Description("The swing mode to set (e.g., 'on', 'off', 'Auto', 'Position 1')")] string swingMode)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("swing_mode", swingMode);
        var start = Stopwatch.GetTimestamp();
        ClimateControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["swing_mode"] = swingMode
            };

            await _homeAssistantClient.CallServiceAsync("climate", "set_swing_mode", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set swing mode for {EntityId} to {SwingMode}", entityId, swingMode);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set swing mode for '{entityId}' to '{swingMode}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting swing mode for {EntityId}", entityId);
            ClimateControlFailures.Add(1);
            return $"Failed to set swing mode for '{entityId}': {ex.Message}";
        }
        finally
        {
            ClimateControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
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

    private async Task RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_embeddingService is null)
        {
            _logger.LogWarning("Skipping climate cache refresh — no embedding provider available.");
            return;
        }

        using var activity = ActivitySource.StartActivity();
        var start = Stopwatch.GetTimestamp();
        try
        {
            // Try Redis cache first (device data only — areas come from IEntityLocationService)
            var cached = await _deviceCache.GetCachedClimateDevicesAsync(cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                var allEmbeddingsFound = true;
                foreach (var device in cached)
                {
                    var embedding = await _deviceCache.GetEmbeddingAsync($"climate:{device.EntityId}", cancellationToken).ConfigureAwait(false);
                    if (embedding is not null)
                    {
                        device.NameEmbedding = embedding;
                    }
                    else
                    {
                        allEmbeddingsFound = false;
                        break;
                    }
                }

                if (allEmbeddingsFound)
                {
                    _cachedDevices = [.. cached];
                    Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);
                    _logger.LogInformation("Loaded {Count} climate devices from Redis cache", cached.Count);
                    return;
                }
            }

            _logger.LogDebug("Refreshing climate cache from Home Assistant...");

            var allStates = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken).ConfigureAwait(false);

            var climateEntities = allStates
                .Where(s => s.EntityId.StartsWith("climate."))
                .ToList();

            var newDevices = new List<ClimateEntity>();

            foreach (var entity in climateEntities)
            {
                var friendlyName = entity.Attributes.TryGetValue("friendly_name", out var nameObj)
                    ? nameObj?.ToString() ?? entity.EntityId
                    : entity.EntityId;

                var hvacModes = ParseStringListAttribute(entity.Attributes, "hvac_modes");
                var fanModes = ParseStringListAttribute(entity.Attributes, "fan_modes");
                var swingModes = ParseStringListAttribute(entity.Attributes, "swing_modes");
                var presetModes = ParseStringListAttribute(entity.Attributes, "preset_modes");

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

                // Resolve area from the shared entity location service
                var areaInfo = _locationService.GetAreaForEntity(entity.EntityId);

                var embedding = await _embeddingService.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false);

                newDevices.Add(new ClimateEntity
                {
                    EntityId = entity.EntityId,
                    FriendlyName = friendlyName,
                    NameEmbedding = embedding,
                    Area = areaInfo?.Name,
                    HvacModes = hvacModes,
                    FanModes = fanModes,
                    SwingModes = swingModes,
                    PresetModes = presetModes,
                    MinTemp = minTemp,
                    MaxTemp = maxTemp,
                    MinHumidity = minHumidity,
                    MaxHumidity = maxHumidity,
                    SupportedFeatures = supportedFeatures
                });
            }

            _cachedDevices = [.. newDevices];
            Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);

            var ttl = TimeSpan.FromMinutes(30);
            var embedTtl = TimeSpan.FromHours(24);
            await _deviceCache.SetCachedClimateDevicesAsync(newDevices, ttl, cancellationToken).ConfigureAwait(false);
            foreach (var device in newDevices.Where(d => d.NameEmbedding is not null))
                await _deviceCache.SetEmbeddingAsync($"climate:{device.EntityId}", device.NameEmbedding!, embedTtl, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Cached {Count} climate devices", newDevices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing climate cache");
        }
        finally
        {
            CacheRefreshDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private async Task EnsureCacheIsCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) > _cacheRefreshInterval)
        {
            if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return;
            try
            {
                if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) > _cacheRefreshInterval)
                    await RefreshCacheAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }

    /// <summary>
    /// Safely parses a JSON array attribute that may contain strings or numbers.
    /// HA devices like SmartThings can return numeric fan_modes (e.g., [4,1,2,3]) instead of strings.
    /// </summary>
    private static List<string> ParseStringListAttribute(Dictionary<string, object> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var value))
            return [];

        try
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind != JsonValueKind.Array)
                    return [];

                return jsonElement.EnumerateArray()
                    .Select(item => item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Number => item.GetRawText(),
                        _ => item.GetRawText()
                    })
                    .OfType<string>()
                    .ToList();
            }

            // Fallback: try deserializing the string representation
            var json = value?.ToString() ?? "[]";
            var modes = JsonSerializer.Deserialize<JsonElement>(json);
            if (modes.ValueKind == JsonValueKind.Array)
            {
                return modes.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Swallow parse failures — return empty list
        }

        return [];
    }
}
