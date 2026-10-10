
namespace lucia.Agents.Auth;

/// <summary>
/// Represents a stored API key. The plaintext key is never stored —
/// only the SHA-256 hash. The key prefix (first 8 chars) is kept for identification.
/// </summary>
public sealed class ApiKeyEntry
{
    /// <summary>
    /// Document ID.
    /// </summary>
    public string Id { get; init; } = null!;

    /// <summary>
    /// SHA-256 hash of the full API key. Used for validation lookups.
    /// </summary>
    public string KeyHash { get; set; } = null!;

    /// <summary>
    /// First 8 characters of the key for display/identification (e.g., "lk_a3b9...").
    /// </summary>
    public string KeyPrefix { get; set; } = null!;

    /// <summary>
    /// User-assigned label (e.g., "Dashboard", "Home Assistant", "My Laptop").
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// When this key was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Historical last-used authentication timestamp. Validation does not update this field.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Optional expiration. Null means the key never expires.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Soft-delete flag. Revoked keys are kept for audit trail.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When this key was revoked. Null if still active.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Permission scopes. For now always ["*"] (full access).
    /// Future: granular permissions like ["read:config", "write:agents"].
    /// </summary>
    public string[] Scopes { get; set; } = ["*"];
}
