using lucia.Agents.Auth;

namespace lucia.Agents.Abstractions;

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

    /// <summary>
    /// Ensures the given plaintext key is the active key with the given name, overriding any
    /// existing key. Bypasses the normal lockout guard because a replacement is always created.
    /// <list type="bullet">
    ///   <item>If a non-revoked key with <paramref name="name"/> already hashes to
    ///         <paramref name="plaintextKey"/>, returns <c>(null, 0)</c> — no change needed.</item>
    ///   <item>Otherwise, revokes ALL non-revoked keys with <paramref name="name"/> and creates a
    ///         fresh one from <paramref name="plaintextKey"/>. Returns <c>(newKey, revokedCount)</c>
    ///         where <c>revokedCount == 0</c> means first-time create and <c>&gt; 0</c> means reset.</item>
    /// </list>
    /// This operation is idempotent: concurrent calls with the same plaintext converge to exactly
    /// one active key and do not throw.
    /// </summary>
    Task<(ApiKeyCreateResponse? Created, int RevokedCount)> OverrideKeyFromPlaintextAsync(
        string name, string plaintextKey, CancellationToken cancellationToken = default);
}
