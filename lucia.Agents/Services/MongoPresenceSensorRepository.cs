using lucia.Agents.Models;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace lucia.Agents.Services;

/// <summary>
/// MongoDB implementation of presence sensor mapping persistence.
/// Uses the "luciaconfig" database with "presence_sensor_mappings" and "presence_config" collections.
/// </summary>
public sealed class MongoPresenceSensorRepository : IPresenceSensorRepository
{
    private const string DatabaseName = "luciaconfig";
    private const string MappingsCollectionName = "presence_sensor_mappings";
    private const string ConfigCollectionName = "presence_config";

    private readonly IMongoCollection<PresenceSensorMapping> _mappings;
    private readonly IMongoCollection<PresenceConfigEntry> _config;

    public MongoPresenceSensorRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(DatabaseName);
        _mappings = db.GetCollection<PresenceSensorMapping>(MappingsCollectionName);
        _config = db.GetCollection<PresenceConfigEntry>(ConfigCollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _mappings.Indexes.CreateMany([
            new CreateIndexModel<PresenceSensorMapping>(
                Builders<PresenceSensorMapping>.IndexKeys.Ascending(m => m.AreaId)),
            new CreateIndexModel<PresenceSensorMapping>(
                Builders<PresenceSensorMapping>.IndexKeys.Ascending(m => m.IsUserOverride)),
        ]);
    }

    public async Task<IReadOnlyList<PresenceSensorMapping>> GetAllMappingsAsync(CancellationToken ct = default)
    {
        var cursor = await _mappings.FindAsync(
            Builders<PresenceSensorMapping>.Filter.Empty, cancellationToken: ct).ConfigureAwait(false);
        return await cursor.ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task ReplaceAutoDetectedMappingsAsync(
        IReadOnlyList<PresenceSensorMapping> autoDetected,
        CancellationToken ct = default)
    {
        // Delete all non-user-override mappings, then insert the new auto-detected ones
        await _mappings.DeleteManyAsync(
            Builders<PresenceSensorMapping>.Filter.Eq(m => m.IsUserOverride, false),
            ct).ConfigureAwait(false);

        if (autoDetected.Count > 0)
        {
            await _mappings.InsertManyAsync(autoDetected, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    public async Task UpsertMappingAsync(PresenceSensorMapping mapping, CancellationToken ct = default)
    {
        await _mappings.ReplaceOneAsync(
            Builders<PresenceSensorMapping>.Filter.Eq(m => m.EntityId, mapping.EntityId),
            mapping,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    public async Task DeleteMappingAsync(string entityId, CancellationToken ct = default)
    {
        await _mappings.DeleteOneAsync(
            Builders<PresenceSensorMapping>.Filter.Eq(m => m.EntityId, entityId),
            ct).ConfigureAwait(false);
    }

    public async Task<bool> GetEnabledAsync(CancellationToken ct = default)
    {
        var entry = await _config.Find(
            Builders<PresenceConfigEntry>.Filter.Eq(c => c.Key, PresenceConfigEntry.EnabledKey))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return entry?.Enabled ?? true; // enabled by default
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        var entry = new PresenceConfigEntry
        {
            Key = PresenceConfigEntry.EnabledKey,
            Enabled = enabled,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _config.ReplaceOneAsync(
            Builders<PresenceConfigEntry>.Filter.Eq(c => c.Key, PresenceConfigEntry.EnabledKey),
            entry,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Simple config entry for presence detection global settings.
    /// </summary>
    internal sealed class PresenceConfigEntry
    {
        public const string EnabledKey = "presence_detection_enabled";

        [BsonId]
        public required string Key { get; init; }

        public bool Enabled { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }
}
