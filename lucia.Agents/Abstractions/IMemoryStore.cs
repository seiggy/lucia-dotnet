using lucia.Agents.Models;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Provides per-user memory storage for conversation context and preferences.
/// </summary>
public interface IMemoryStore
{
    /// <summary>
    /// Stores or replaces a memory entry for a user.
    /// </summary>
    Task StoreAsync(string userId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default);

    /// <summary>
    /// Stores or replaces a memory entry for a user with an absolute expiration timestamp.
    /// The supplied deadline is written directly, without converting it to a relative TTL.
    /// </summary>
    Task StoreAsync(string userId, string key, string value, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a memory value for a user by key.
    /// </summary>
    Task<string?> RetrieveAsync(string userId, string key, CancellationToken ct = default);

    /// <summary>
    /// Searches a user's memories by key or value.
    /// A nonblank query is a literal substring, not a wildcard pattern.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> SearchAsync(string userId, string? query = null, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Searches personal memories, excluding reserved chat-history keys before applying the limit.
    /// A nonblank query is a literal substring of the key or value, not a wildcard pattern.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> SearchPersonalAsync(string userId, string? query = null, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Deletes a memory for a user by key.
    /// </summary>
    Task DeleteAsync(string userId, string key, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all active memories for a user.
    /// </summary>
    Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default);
}
