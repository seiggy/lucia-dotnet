using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Abstraction for reading and writing configuration entries to a persistent store.
/// Supports both admin UI writes and seed operations.
/// </summary>
public interface IConfigStoreWriter
{
    /// <summary>
    /// Upserts a configuration entry. Triggers config provider hot-reload on next poll.
    /// </summary>
    Task SetAsync(
        string key,
        string? value,
        string updatedBy = "setup-wizard",
        bool isSensitive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a config value directly from the store (bypasses cached IConfiguration).
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the count of all configuration entries in the store.
    /// </summary>
    Task<long> GetEntryCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all configuration keys currently in the store.
    /// </summary>
    Task<IReadOnlySet<string>> GetAllKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts multiple configuration entries at once (for seeding).
    /// </summary>
    Task InsertManyAsync(IReadOnlyList<ConfigEntry> entries, CancellationToken cancellationToken = default);
}
