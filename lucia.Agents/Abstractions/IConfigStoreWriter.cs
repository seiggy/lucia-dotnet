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

    /// <summary>
    /// Returns all configuration entries in the store (including metadata).
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetAllEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all configuration entries belonging to a specific section.
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetEntriesBySectionAsync(string section, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all configuration entries whose key starts with the given prefix.
    /// </summary>
    Task<IReadOnlyList<ConfigEntry>> GetEntriesByKeyPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all configuration entries. Returns the number of deleted entries.
    /// </summary>
    Task<long> DeleteAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all configuration entries whose key starts with the given prefix. Returns the number of deleted entries.
    /// </summary>
    Task<long> DeleteByKeyPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);
}
