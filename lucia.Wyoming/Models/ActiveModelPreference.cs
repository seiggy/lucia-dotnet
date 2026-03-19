using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Wyoming.Models;

/// <summary>
/// Persisted user preference for an active model override, keyed by engine type.
/// Stored in MongoDB so model selections survive reboots.
/// </summary>
public sealed class ActiveModelPreference
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public required string EngineType { get; init; }

    public required string ModelId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
