using lucia.Agents.Models;

namespace lucia.Agents.Services;

/// <summary>
/// Persistence for presence sensor mappings and configuration.
/// Stores auto-detected and user-overridden sensor-to-area mappings in MongoDB.
/// </summary>
public interface IPresenceSensorRepository
{
    /// <summary>
    /// Get all persisted sensor mappings (both auto-detected and user overrides).
    /// </summary>
    Task<IReadOnlyList<PresenceSensorMapping>> GetAllMappingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Replace all auto-detected mappings while preserving user overrides.
    /// Called during sensor re-scan to refresh auto-detected sensors
    /// without losing manual user configuration.
    /// </summary>
    Task ReplaceAutoDetectedMappingsAsync(
        IReadOnlyList<PresenceSensorMapping> autoDetected,
        CancellationToken ct = default);

    /// <summary>
    /// Upsert a single sensor mapping (for user override operations).
    /// </summary>
    Task UpsertMappingAsync(PresenceSensorMapping mapping, CancellationToken ct = default);

    /// <summary>
    /// Delete a sensor mapping by entity ID.
    /// </summary>
    Task DeleteMappingAsync(string entityId, CancellationToken ct = default);

    /// <summary>
    /// Get the global enabled/disabled state for presence detection.
    /// Returns true if not explicitly set (enabled by default).
    /// </summary>
    Task<bool> GetEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Set the global enabled/disabled state for presence detection.
    /// </summary>
    Task SetEnabledAsync(bool enabled, CancellationToken ct = default);
}
