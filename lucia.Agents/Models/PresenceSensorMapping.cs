namespace lucia.Agents.Models;

/// <summary>
/// Maps a Home Assistant presence sensor entity to an area with a confidence level.
/// Auto-detected by pattern matching, with user overrides persisted in MongoDB.
/// </summary>
public sealed class PresenceSensorMapping
{
    /// <summary>Home Assistant entity ID (e.g., "sensor.satellite1_91b604_presence_target_count").</summary>
    public required string EntityId { get; init; }

    /// <summary>Home Assistant area ID this sensor is mapped to.</summary>
    public required string AreaId { get; init; }

    /// <summary>Area friendly name (denormalized for display).</summary>
    public string? AreaName { get; set; }

    /// <summary>Confidence level assigned to this sensor based on its type.</summary>
    public PresenceConfidence Confidence { get; set; }

    /// <summary>
    /// Whether this mapping was created by auto-detection (false) or manually overridden by the user (true).
    /// User overrides take precedence over auto-detection during refresh.
    /// </summary>
    public bool IsUserOverride { get; set; }

    /// <summary>
    /// Whether this sensor is excluded from presence calculations.
    /// Users can disable specific sensors that produce unreliable data.
    /// </summary>
    public bool IsDisabled { get; set; }
}
