namespace lucia.Agents.Auth;

/// <summary>
/// Service for creating, validating, listing, and revoking API keys.
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Creates a new API key with the given name. Returns the plaintext key (shown once).
    /// </summary>
    Task<ApiKeyCreateResponse> CreateKeyAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an API key from a provided plaintext (for headless/env seeding).
    /// Returns the response if created; null if a key with that name already exists.
    /// </summary>
    Task<ApiKeyCreateResponse?> CreateKeyFromPlaintextAsync(string name, string plaintextKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an API key. Returns the entry if valid, null if invalid/revoked/expired.
    /// Updates LastUsedAt on success.
    /// </summary>
    Task<ApiKeyEntry?> ValidateKeyAsync(string plaintextKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all API keys (active and revoked) as summaries. Never returns full keys or hashes.
    /// </summary>
    Task<IReadOnlyList<ApiKeySummary>> ListKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an API key by ID. Returns false if key not found or already revoked.
    /// Throws if this is the last active key (lockout prevention).
    /// </summary>
    Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the existing key and creates a new one with the same name.
    /// Returns the new plaintext key. Throws if key not found.
    /// </summary>
    Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of active (non-revoked, non-expired) keys.
    /// </summary>
    Task<int> GetActiveKeyCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if any API key exists (setup has been started).
    /// </summary>
    Task<bool> HasAnyKeysAsync(CancellationToken cancellationToken = default);
}
