namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Represents the occupancy state of a single area based on presence sensor data.
/// <param name="AreaId">Home Assistant area ID.</param>
/// <param name="AreaName">Human-readable area name.</param>
/// <param name="IsOccupied">Whether presence is currently detected in this area.</param>
/// <param name="OccupantCount">Estimated number of occupants from radar sensors. Null if only binary sensors are available in this area.</param>
/// <param name="Confidence">Confidence level of the presence reading, based on hte best sensor available.</param>
/// </summary>
public sealed record OccupiedArea(
    string AreaId,
    string AreaName,
    bool IsOccupied,
    int? OccupantCount,
    PresenceConfidence Confidence
);
