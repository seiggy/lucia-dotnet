using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Agents.Configuration;

/// <summary>
/// Represents a single configuration entry persisted to MongoDB.
/// Uses flat ASP.NET Core configuration key format (e.g., "HomeAssistant:BaseUrl").
/// </summary>
public sealed class ConfigEntry
{
    /// <summary>
    /// MongoDB document ID — uses the configuration key as the natural ID.
    /// Example: "HomeAssistant:BaseUrl"
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string Key { get; set; } = default!;

    /// <summary>
    /// The configuration value as a string. ASP.NET Core configuration is string-based.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// The top-level section this key belongs to (e.g., "HomeAssistant", "ConnectionStrings").
    /// Derived from the key prefix before the first colon. Used for querying by section.
    /// </summary>
    public string Section { get; set; } = default!;

    /// <summary>
    /// UTC timestamp of when this entry was last modified.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who last modified this entry (e.g., "system-seed", "admin-ui").
    /// </summary>
    public string UpdatedBy { get; set; } = "system";

    /// <summary>
    /// Whether this value contains sensitive data (tokens, keys, passwords).
    /// Sensitive values are masked when returned from the API.
    /// </summary>
    public bool IsSensitive { get; set; }

    /// <summary>
    /// Collection name constant for MongoDB.
    /// </summary>
    public const string CollectionName = "configuration";

    /// <summary>
    /// Database name constant for MongoDB.
    /// </summary>
    public const string DatabaseName = "luciaconfig";
}
