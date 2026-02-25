namespace lucia.Agents.Models;

/// <summary>
/// Represents the occupancy state of a single area based on presence sensor data.
/// </summary>
public sealed record OccupiedArea(
    /// <summary>Home Assistant area ID.</summary>
    string AreaId,

    /// <summary>Human-readable area name.</summary>
    string AreaName,

    /// <summary>Whether presence is currently detected in this area.</summary>
    bool IsOccupied,

    /// <summary>
    /// Estimated number of occupants from radar sensors.
    /// Null if only binary sensors are available for this area.
    /// </summary>
    int? OccupantCount,

    /// <summary>
    /// Confidence level of the presence reading, based on the best sensor available.
    /// </summary>
    PresenceConfidence Confidence
);
