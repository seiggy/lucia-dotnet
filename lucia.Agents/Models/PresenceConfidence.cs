namespace lucia.Agents.Models;

/// <summary>
/// Confidence level for presence detection, based on sensor technology.
/// Higher confidence sensors provide more reliable real-time occupancy data.
/// </summary>
public enum PresenceConfidence
{
    /// <summary>
    /// No presence data available for this area.
    /// </summary>
    None = 0,

    /// <summary>
    /// Low confidence — occupancy sensors (e.g., Ecobee thermostats) that derive
    /// occupancy from motion tracking with long hold times (often hours).
    /// These lag significantly and may report "occupied" long after everyone has left.
    /// Pattern: binary_sensor with device_class: occupancy and name matching *occupancy*
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium confidence — PIR/motion sensors that detect movement but not stillness.
    /// A person sitting still won't trigger them. Typically short timeout.
    /// Pattern: binary_sensor with device_class: motion
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High confidence — mmWave radar sensors (binary mode).
    /// Detects presence even when stationary but only reports on/off.
    /// Pattern: binary_sensor.*presence
    /// </summary>
    High = 3,

    /// <summary>
    /// Highest confidence — mmWave radar sensors with target counting.
    /// Knows exactly how many people are in a room in real-time.
    /// Pattern: sensor.*presence* with numeric state
    /// </summary>
    Highest = 4
}
