using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Agents.Auth;

/// <summary>
/// Represents an API key stored in MongoDB. The plaintext key is never stored —
/// only the SHA-256 hash. The key prefix (first 8 chars) is kept for identification.
/// </summary>
public sealed class ApiKeyEntry
{
    /// <summary>
    /// MongoDB document ID.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = null!;

    /// <summary>
    /// SHA-256 hash of the full API key. Used for validation lookups.
    /// </summary>
    [BsonElement("keyHash")]
    public string KeyHash { get; set; } = null!;

    /// <summary>
    /// First 8 characters of the key for display/identification (e.g., "lk_a3b9...").
    /// </summary>
    [BsonElement("keyPrefix")]
    public string KeyPrefix { get; set; } = null!;

    /// <summary>
    /// User-assigned label (e.g., "Dashboard", "Home Assistant", "My Laptop").
    /// </summary>
    [BsonElement("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// When this key was created.
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this key was last used for authentication. Null if never used.
    /// </summary>
    [BsonElement("lastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Optional expiration. Null means the key never expires.
    /// </summary>
    [BsonElement("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Soft-delete flag. Revoked keys are kept for audit trail.
    /// </summary>
    [BsonElement("isRevoked")]
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When this key was revoked. Null if still active.
    /// </summary>
    [BsonElement("revokedAt")]
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Permission scopes. For now always ["*"] (full access).
    /// Future: granular permissions like ["read:config", "write:agents"].
    /// </summary>
    [BsonElement("scopes")]
    public string[] Scopes { get; set; } = ["*"];

    /// <summary>
    /// MongoDB collection name for API keys.
    /// </summary>
    public const string CollectionName = "api_keys";

    /// <summary>
    /// Database name (shared with config store).
    /// </summary>
    public const string DatabaseName = "luciaconfig";
}
