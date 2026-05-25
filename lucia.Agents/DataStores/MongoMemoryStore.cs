using System.Text.RegularExpressions;

using lucia.Agents.Abstractions;
using lucia.Agents.Models;

using MongoDB.Bson;
using MongoDB.Driver;

namespace lucia.Agents.DataStores;

/// <summary>
/// MongoDB-backed implementation of <see cref="IMemoryStore"/>.
/// </summary>
public sealed class MongoMemoryStore : IMemoryStore
{
    private const string DatabaseName = "luciaconfig";
    private const string CollectionName = "user_memories";

    private readonly IMongoCollection<BsonDocument> _collection;

    /// <summary>
    /// Initializes a new instance of the <see cref="MongoMemoryStore"/> class.
    /// </summary>
    public MongoMemoryStore(IMongoClient mongoClient)
    {
        var database = mongoClient.GetDatabase(DatabaseName);
        _collection = database.GetCollection<BsonDocument>(CollectionName);
        EnsureIndexes();
    }

    /// <inheritdoc/>
    public async Task StoreAsync(string userId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var createdAt = DateTime.UtcNow;
        var expiresAt = ttl.HasValue ? createdAt.Add(ttl.Value) : (DateTime?)null;

        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId)
            & Builders<BsonDocument>.Filter.Eq("key", key);
        var updates = new List<UpdateDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Update.Set("user_id", userId),
            Builders<BsonDocument>.Update.Set("key", key),
            Builders<BsonDocument>.Update.Set("value", value),
            Builders<BsonDocument>.Update.Set("created_at", createdAt),
            expiresAt.HasValue
                ? Builders<BsonDocument>.Update.Set("expires_at", expiresAt.Value)
                : Builders<BsonDocument>.Update.Set("expires_at", BsonNull.Value),
        };

        await _collection.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Combine(updates), new UpdateOptions { IsUpsert = true }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> RetrieveAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId)
            & Builders<BsonDocument>.Filter.Eq("key", key)
            & ActiveFilter(DateTime.UtcNow);
        var memory = await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return memory is null ? null : memory["value"].AsString;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(string userId, string? query = null, int limit = 20, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (limit <= 0)
        {
            return [];
        }

        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId) & ActiveFilter(DateTime.UtcNow);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var escapedQuery = Regex.Escape(query);
            var regex = new BsonRegularExpression(escapedQuery, "i");
            filter &= Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Regex("key", regex),
                Builders<BsonDocument>.Filter.Regex("value", regex));
        }

        var documents = await _collection.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
            .Limit(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return documents.Select(Map).ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId)
            & Builders<BsonDocument>.Filter.Eq("key", key);
        await _collection.DeleteOneAsync(filter, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId) & ActiveFilter(DateTime.UtcNow);
        var documents = await _collection.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return documents.Select(Map).ToList();
    }

    private void EnsureIndexes()
    {
        _collection.Indexes.CreateMany(
        [
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("user_id").Ascending("key"),
                new CreateIndexOptions { Unique = true, Name = "idx_user_memory_key" }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expires_at"),
                new CreateIndexOptions
                {
                    Name = "idx_user_memory_expires_at",
                    ExpireAfter = TimeSpan.Zero // TTL index: MongoDB deletes docs when expires_at < now
                }),
        ]);
    }

    private static FilterDefinition<BsonDocument> ActiveFilter(DateTime now)
    {
        return Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("expires_at", BsonNull.Value),
            Builders<BsonDocument>.Filter.Exists("expires_at", false),
            Builders<BsonDocument>.Filter.Gt("expires_at", now));
    }

    private static MemoryEntry Map(BsonDocument document)
    {
        var expiresAt = document.TryGetValue("expires_at", out var expiresValue) && !expiresValue.IsBsonNull
            ? expiresValue.ToUniversalTime()
            : (DateTime?)null;

        return new MemoryEntry(
            document["key"].AsString,
            document["value"].AsString,
            document["created_at"].ToUniversalTime(),
            expiresAt);
    }
}
