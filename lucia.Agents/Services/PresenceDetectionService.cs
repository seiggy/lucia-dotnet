using lucia.Agents.Models;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Discovers presence sensors from Home Assistant, maps them to areas via
/// <see cref="IEntityLocationService"/>, and provides real-time occupancy state
/// by querying HA entity states.
///
/// Auto-detection patterns (from most to least confident):
/// <list type="bullet">
///   <item><description>Highest: sensor.*presence* with numeric state (mmWave radar target count)</description></item>
///   <item><description>High: binary_sensor.*presence (mmWave radar binary)</description></item>
///   <item><description>Medium: binary_sensor with device_class=motion</description></item>
///   <item><description>Low: binary_sensor with device_class=occupancy and name matching *occupancy*</description></item>
/// </list>
/// </summary>
public sealed partial class PresenceDetectionService : IPresenceDetectionService
{
    private readonly IHomeAssistantClient _haClient;
    private readonly IEntityLocationService _locationService;
    private readonly IPresenceSensorRepository _repository;
    private readonly ILogger<PresenceDetectionService> _logger;

    // In-memory cache refreshed by RefreshSensorMappingsAsync
    private volatile IReadOnlyList<PresenceSensorMapping> _cachedMappings = [];

    public PresenceDetectionService(
        IHomeAssistantClient haClient,
        IEntityLocationService locationService,
        IPresenceSensorRepository repository,
        ILogger<PresenceDetectionService> logger)
    {
        _haClient = haClient;
        _locationService = locationService;
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool?> IsOccupiedAsync(string areaId, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct).ConfigureAwait(false))
            return null;

        var mappings = GetActiveMappingsForArea(areaId);
        if (mappings.Count == 0)
            return null;

        var states = await _haClient.GetStatesAsync(ct).ConfigureAwait(false);
        var stateMap = BuildStateMap(states);

        // Use the highest-confidence sensor that has a known state
        foreach (var mapping in mappings.OrderByDescending(m => m.Confidence))
        {
            if (!stateMap.TryGetValue(mapping.EntityId, out var state))
                continue;

            var occupied = InterpretSensorState(state, mapping.Confidence);
            if (occupied.HasValue)
                return occupied.Value;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<int?> GetOccupantCountAsync(string areaId, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct).ConfigureAwait(false))
            return null;

        var mappings = GetActiveMappingsForArea(areaId);

        // Only Highest-confidence sensors (mmWave target count) provide occupant counts
        var countSensors = mappings
            .Where(m => m.Confidence == PresenceConfidence.Highest)
            .ToList();

        if (countSensors.Count == 0)
            return null;

        var states = await _haClient.GetStatesAsync(ct).ConfigureAwait(false);
        var stateMap = BuildStateMap(states);

        foreach (var sensor in countSensors)
        {
            if (!stateMap.TryGetValue(sensor.EntityId, out var state))
                continue;

            if (int.TryParse(state.State, out var count))
                return count;
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OccupiedArea>> GetOccupiedAreasAsync(CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(ct).ConfigureAwait(false))
            return [];

        var mappings = _cachedMappings.Where(m => !m.IsDisabled).ToList();
        if (mappings.Count == 0)
            return [];

        var states = await _haClient.GetStatesAsync(ct).ConfigureAwait(false);
        var stateMap = BuildStateMap(states);
        var areas = await _locationService.GetAreasAsync(ct).ConfigureAwait(false);
        var areaLookup = areas.ToDictionary(a => a.AreaId, StringComparer.OrdinalIgnoreCase);

        // Group sensors by area and evaluate each area
        var areaGroups = mappings.GroupBy(m => m.AreaId, StringComparer.OrdinalIgnoreCase);
        var results = new List<OccupiedArea>();

        foreach (var group in areaGroups)
        {
            var areaId = group.Key;
            var areaName = areaLookup.TryGetValue(areaId, out var area)
                ? area.Name
                : group.First().AreaName ?? areaId;

            // Find the best sensor by confidence
            var bestMapping = group
                .OrderByDescending(m => m.Confidence)
                .FirstOrDefault(m => stateMap.ContainsKey(m.EntityId));

            if (bestMapping is null)
                continue;

            var state = stateMap[bestMapping.EntityId];
            var isOccupied = InterpretSensorState(state, bestMapping.Confidence) ?? false;

            // Try to get occupant count from Highest-confidence sensors
            int? occupantCount = null;
            var countSensor = group
                .Where(m => m.Confidence == PresenceConfidence.Highest)
                .FirstOrDefault(m => stateMap.ContainsKey(m.EntityId));

            if (countSensor is not null && int.TryParse(stateMap[countSensor.EntityId].State, out var count))
            {
                occupantCount = count;
                // If we have a count sensor, presence is determined by count > 0
                isOccupied = count > 0;
            }

            if (isOccupied)
            {
                results.Add(new OccupiedArea(
                    areaId,
                    areaName,
                    IsOccupied: true,
                    occupantCount,
                    bestMapping.Confidence));
            }
        }

        return results.OrderByDescending(r => r.Confidence).ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PresenceSensorMapping>> GetSensorMappingsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_cachedMappings);
    }

    /// <inheritdoc />
    public async Task RefreshSensorMappingsAsync(CancellationToken ct = default)
    {
        LogRefreshStarted();

        var states = await _haClient.GetStatesAsync(ct).ConfigureAwait(false);
        var areas = await _locationService.GetAreasAsync(ct).ConfigureAwait(false);

        var autoDetected = DiscoverPresenceSensors(states, areas);

        LogSensorsDiscovered(autoDetected.Count);

        // Persist auto-detected mappings (preserves user overrides)
        await _repository.ReplaceAutoDetectedMappingsAsync(autoDetected, ct).ConfigureAwait(false);

        // Reload all mappings (auto + user overrides) into memory
        _cachedMappings = await _repository.GetAllMappingsAsync(ct).ConfigureAwait(false);

        LogRefreshCompleted(_cachedMappings.Count);
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        return await _repository.GetEnabledAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await _repository.SetEnabledAsync(enabled, ct).ConfigureAwait(false);
        LogEnabledChanged(enabled);
    }

    /// <summary>
    /// Auto-discover presence sensors from HA states and map them to areas.
    /// </summary>
    internal List<PresenceSensorMapping> DiscoverPresenceSensors(
        HomeAssistant.Models.HomeAssistantState[] states,
        IReadOnlyList<AreaInfo> areas)
    {
        var mappings = new List<PresenceSensorMapping>();
        var areaLookup = areas.ToDictionary(a => a.AreaId, StringComparer.OrdinalIgnoreCase);

        foreach (var state in states)
        {
            var confidence = ClassifySensor(state);
            if (confidence == PresenceConfidence.None)
                continue;

            // Find the area for this entity
            var area = _locationService.GetAreaForEntity(state.EntityId);
            if (area is null)
                continue;

            mappings.Add(new PresenceSensorMapping
            {
                EntityId = state.EntityId,
                AreaId = area.AreaId,
                AreaName = area.Name,
                Confidence = confidence,
                IsUserOverride = false,
                IsDisabled = false
            });
        }

        return mappings;
    }

    /// <summary>
    /// Classify a HA entity as a presence sensor with a confidence level.
    /// Returns <see cref="PresenceConfidence.None"/> if the entity is not a presence sensor.
    /// </summary>
    internal static PresenceConfidence ClassifySensor(HomeAssistant.Models.HomeAssistantState state)
    {
        var entityId = state.EntityId;
        var deviceClass = state.Attributes.TryGetValue("device_class", out var dc) ? dc?.ToString() : null;

        // Highest: sensor.*presence* with numeric state (mmWave radar target count)
        if (entityId.StartsWith("sensor.", StringComparison.OrdinalIgnoreCase)
            && entityId.Contains("presence", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(state.State, out _))
        {
            return PresenceConfidence.Highest;
        }

        // High: binary_sensor.*presence (mmWave radar binary)
        if (entityId.StartsWith("binary_sensor.", StringComparison.OrdinalIgnoreCase)
            && entityId.Contains("presence", StringComparison.OrdinalIgnoreCase))
        {
            return PresenceConfidence.High;
        }

        // Medium: binary_sensor with device_class=motion
        if (entityId.StartsWith("binary_sensor.", StringComparison.OrdinalIgnoreCase)
            && string.Equals(deviceClass, "motion", StringComparison.OrdinalIgnoreCase))
        {
            return PresenceConfidence.Medium;
        }

        // Low: binary_sensor with device_class=occupancy
        if (entityId.StartsWith("binary_sensor.", StringComparison.OrdinalIgnoreCase)
            && string.Equals(deviceClass, "occupancy", StringComparison.OrdinalIgnoreCase))
        {
            return PresenceConfidence.Low;
        }

        return PresenceConfidence.None;
    }

    /// <summary>
    /// Interpret a sensor state value into a presence boolean.
    /// </summary>
    private static bool? InterpretSensorState(
        HomeAssistant.Models.HomeAssistantState state,
        PresenceConfidence confidence)
    {
        // Numeric sensors (Highest confidence) — count > 0 means occupied
        if (confidence == PresenceConfidence.Highest)
        {
            return int.TryParse(state.State, out var count) ? count > 0 : null;
        }

        // Binary sensors — "on" means detected
        return state.State?.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<PresenceSensorMapping> GetActiveMappingsForArea(string areaId)
    {
        return _cachedMappings
            .Where(m => !m.IsDisabled
                && string.Equals(m.AreaId, areaId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static Dictionary<string, HomeAssistant.Models.HomeAssistantState> BuildStateMap(
        HomeAssistant.Models.HomeAssistantState[] states)
    {
        return states.ToDictionary(
            s => s.EntityId,
            StringComparer.OrdinalIgnoreCase);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshing presence sensor mappings from Home Assistant")]
    private partial void LogRefreshStarted();

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-discovered {Count} presence sensors")]
    private partial void LogSensorsDiscovered(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Presence sensor refresh complete — {Count} total mappings (auto + user overrides)")]
    private partial void LogRefreshCompleted(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Presence detection {Action}")]
    private partial void LogEnabledChanged(bool action);
}
