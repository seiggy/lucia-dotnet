using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using MongoDB.Driver;

namespace lucia.Agents.Auth;

/// <summary>
/// Helper service for reading and writing config entries during setup and auth operations.
/// Wraps the MongoDB configuration collection with typed write operations.
/// </summary>
public sealed class ConfigStoreWriter : IConfigStoreWriter
{
    private readonly IMongoCollection<ConfigEntry> _collection;

    public ConfigStoreWriter(IMongoClient mongoClient)
    {
        var database = mongoClient.GetDatabase(ConfigEntry.DatabaseName);
        _collection = database.GetCollection<ConfigEntry>(ConfigEntry.CollectionName);
    }

    /// <summary>
    /// Upserts a configuration entry. Triggers MongoConfigurationProvider hot-reload on next poll.
    /// </summary>
    public async Task SetAsync(
        string key,
        string? value,
        string updatedBy = "setup-wizard",
        bool isSensitive = false,
        CancellationToken cancellationToken = default)
    {
        var section = key.Contains(':') ? key[..key.IndexOf(':')] : key;

        var filter = Builders<ConfigEntry>.Filter.Eq(e => e.Key, key);
        var update = Builders<ConfigEntry>.Update
            .Set(e => e.Value, value)
            .Set(e => e.Section, section)
            .Set(e => e.UpdatedAt, DateTime.UtcNow)
            .Set(e => e.UpdatedBy, updatedBy)
            .Set(e => e.IsSensitive, isSensitive);

        await _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a config value directly from MongoDB (bypasses cached IConfiguration).
    /// </summary>
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var entry = await _collection
            .Find(Builders<ConfigEntry>.Filter.Eq(e => e.Key, key))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entry?.Value;
    }

    /// <inheritdoc />
    public async Task<long> GetEntryCountAsync(CancellationToken cancellationToken = default)
    {
        return await _collection.CountDocumentsAsync(
            FilterDefinition<ConfigEntry>.Empty,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = await _collection
            .Find(FilterDefinition<ConfigEntry>.Empty)
            .Project(e => e.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task InsertManyAsync(IReadOnlyList<ConfigEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return;

        await _collection.InsertManyAsync(
            entries,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfigEntry>> GetAllEntriesAsync(CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(FilterDefinition<ConfigEntry>.Empty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfigEntry>> GetEntriesBySectionAsync(string section, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ConfigEntry>.Filter.Eq(e => e.Section, section);
        return await _collection
            .Find(filter)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfigEntry>> GetEntriesByKeyPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var escapedPrefix = System.Text.RegularExpressions.Regex.Escape(keyPrefix);
        var filter = Builders<ConfigEntry>.Filter.Regex(e => e.Key, $"^{escapedPrefix}");
        return await _collection
            .Find(filter)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        var result = await _collection
            .DeleteManyAsync(FilterDefinition<ConfigEntry>.Empty, cancellationToken)
            .ConfigureAwait(false);
        return result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<long> DeleteByKeyPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var escapedPrefix = System.Text.RegularExpressions.Regex.Escape(keyPrefix);
        var filter = Builders<ConfigEntry>.Filter.Regex(e => e.Key, $"^{escapedPrefix}");
        var result = await _collection
            .DeleteManyAsync(filter, cancellationToken)
            .ConfigureAwait(false);
        return result.DeletedCount;
    }
}
