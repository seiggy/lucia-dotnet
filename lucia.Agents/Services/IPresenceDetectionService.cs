using lucia.Agents.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Provides room-level occupancy data from Home Assistant presence sensors.
/// Auto-discovers sensors by entity_id patterns and device_class, maps them to areas,
/// and returns occupancy state with confidence levels.
///
/// Graceful degradation: returns null when no presence sensors are available for an area
/// or when the service is globally disabled.
/// </summary>
public interface IPresenceDetectionService
{
    /// <summary>
    /// Returns whether any presence is detected in the specified area.
    /// Returns null if no presence sensors exist for the area (graceful degradation).
    /// </summary>
    Task<bool?> IsOccupiedAsync(string areaId, CancellationToken ct = default);

    /// <summary>
    /// Returns the estimated number of occupants in the area (from radar sensors with target counting).
    /// Returns null if only binary sensors are available or no sensors exist for the area.
    /// </summary>
    Task<int?> GetOccupantCountAsync(string areaId, CancellationToken ct = default);

    /// <summary>
    /// Returns all areas that currently have detected presence, ordered by confidence (highest first).
    /// Empty list if no presence is detected anywhere or the service is disabled.
    /// </summary>
    Task<IReadOnlyList<OccupiedArea>> GetOccupiedAreasAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the presence sensor mappings (auto-detected + user overrides).
    /// Used by the dashboard management page.
    /// </summary>
    Task<IReadOnlyList<PresenceSensorMapping>> GetSensorMappingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Triggers a re-scan of Home Assistant entities to discover and map presence sensors.
    /// Called on startup and when the user requests a refresh from the dashboard.
    /// </summary>
    Task RefreshSensorMappingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether the presence detection service is globally enabled.
    /// Users without presence sensors can disable this to avoid unnecessary scanning.
    /// </summary>
    Task<bool> IsEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Enable or disable the presence detection service globally.
    /// </summary>
    Task SetEnabledAsync(bool enabled, CancellationToken ct = default);
}
