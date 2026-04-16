using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Represents a cached sensor or binary_sensor entity with search capabilities.
/// Mirrors Home Assistant sensor entity attributes.
/// </summary>
public sealed class SensorEntity : IMatchableEntity
{
    public string EntityId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public Embedding<float>? NameEmbedding { get; set; }
    public string? Area { get; set; }

    /// <inheritdoc />
    public string[] PhoneticKeys { get; set; } = [];

    /// <summary>
    /// The Home Assistant device class (e.g., "temperature", "humidity", "motion", "door", "battery").
    /// Null when not set by the integration.
    /// </summary>
    public string? DeviceClass { get; set; }

    /// <summary>
    /// The unit of measurement (e.g., "°F", "%", "lux", "ppm").
    /// Null when not set by the integration.
    /// </summary>
    public string? UnitOfMeasurement { get; set; }

    /// <summary>
    /// The state class from Home Assistant (e.g., "measurement", "total", "total_increasing").
    /// Null when not set by the integration.
    /// </summary>
    public string? StateClass { get; set; }

    /// <summary>
    /// Whether this is a binary_sensor entity (on/off states) vs a regular sensor entity.
    /// </summary>
    public bool IsBinarySensor => EntityId.StartsWith("binary_sensor.");

    // ── IMatchableEntity ────────────────────────────────────────

    string IMatchableEntity.MatchableName => FriendlyName;
    Embedding<float>? IMatchableEntity.NameEmbedding => NameEmbedding;
}