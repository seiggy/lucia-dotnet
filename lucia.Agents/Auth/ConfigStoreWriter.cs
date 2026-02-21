using lucia.Agents.Configuration;
using MongoDB.Driver;

namespace lucia.Agents.Auth;

/// <summary>
/// Helper service for reading and writing config entries during setup and auth operations.
/// Wraps the MongoDB configuration collection with typed write operations.
/// </summary>
public sealed class ConfigStoreWriter
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
}
