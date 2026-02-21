namespace lucia.Agents.Auth;

/// <summary>
/// Response returned when a new API key is created. Contains the plaintext key
/// which is shown to the user exactly once — it cannot be retrieved again.
/// </summary>
public sealed record ApiKeyCreateResponse
{
    /// <summary>
    /// The full plaintext API key (e.g., "lk_x7Ks9mPq2vR4tW8yB3nJ6hD1fA0eC5gL").
    /// Only available at creation time. Store the SHA-256 hash going forward.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// The MongoDB document ID for management operations (revoke, regenerate).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The display prefix (e.g., "lk_x7Ks...").
    /// </summary>
    public required string Prefix { get; init; }

    /// <summary>
    /// The user-assigned name for this key.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the key was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }
}
