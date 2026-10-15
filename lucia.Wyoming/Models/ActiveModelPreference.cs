
namespace lucia.Wyoming.Models;

/// <summary>
/// Persisted user preference for an active model override, keyed by engine type.
/// Stored so model selections survive reboots.
/// </summary>
public sealed class ActiveModelPreference
{
    public required string EngineType { get; init; }

    public required string ModelId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
