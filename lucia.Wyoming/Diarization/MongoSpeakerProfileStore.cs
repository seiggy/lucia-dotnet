using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// MongoDB-backed persistent speaker profile store.
/// Falls back to <see cref="InMemorySpeakerProfileStore"/> when no MongoDB connection is configured.
/// </summary>
public sealed class MongoSpeakerProfileStore : ISpeakerProfileStore
{
    private const string CollectionName = "speaker_profiles";

    private readonly IMongoCollection<SpeakerProfile> _collection;
    private readonly ILogger<MongoSpeakerProfileStore> _logger;

    static MongoSpeakerProfileStore()
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(SpeakerProfile)))
        {
            BsonClassMap.RegisterClassMap<SpeakerProfile>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
            });
        }
    }

    public MongoSpeakerProfileStore(
        IMongoClient mongoClient,
        IOptions<DiarizationOptions> options,
        ILogger<MongoSpeakerProfileStore> logger)
    {
        var db = mongoClient.GetDatabase(options.Value.ProfileStoreDatabaseName);
        _collection = db.GetCollection<SpeakerProfile>(CollectionName);
        _logger = logger;

        EnsureIndexes();
    }

    public async Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var filter = Builders<SpeakerProfile>.Filter.Eq(p => p.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetAllAsync(CancellationToken ct)
    {
        return await _collection
            .Find(Builders<SpeakerProfile>.Filter.Empty)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetProvisionalProfilesAsync(CancellationToken ct)
    {
        var filter = Builders<SpeakerProfile>.Filter.Eq(p => p.IsProvisional, true);
        return await _collection.Find(filter).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct)
    {
        var filter = Builders<SpeakerProfile>.Filter.Eq(p => p.IsProvisional, false);
        return await _collection.Find(filter).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task CreateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            await _collection.InsertOneAsync(profile, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException($"Speaker profile '{profile.Id}' already exists.", ex);
        }
    }

    public async Task UpdateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var filter = Builders<SpeakerProfile>.Filter.Eq(p => p.Id, profile.Id);
        var result = await _collection.ReplaceOneAsync(filter, profile, cancellationToken: ct).ConfigureAwait(false);

        if (result.MatchedCount == 0)
        {
            throw new KeyNotFoundException($"Speaker profile '{profile.Id}' was not found.");
        }
    }

    public async Task<SpeakerProfile?> UpdateAtomicAsync(
        string id,
        Func<SpeakerProfile, SpeakerProfile> transform,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(transform);

        var filter = Builders<SpeakerProfile>.Filter.Eq(p => p.Id, id);
        var existing = await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (existing is null)
        {
            throw new InvalidOperationException($"Profile '{id}' not found");
        }

        var updated = transform(existing);
        var options = new FindOneAndReplaceOptions<SpeakerProfile>
        {
            ReturnDocument = ReturnDocument.After,
        };

        return await _collection
            .FindOneAndReplaceAsync(filter, updated, options, ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var filter = Builders<SpeakerProfile>.Filter.Eq(p => p.Id, id);
        await _collection.DeleteOneAsync(filter, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(
        int retentionDays,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retentionDays);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var filter = Builders<SpeakerProfile>.Filter.And(
            Builders<SpeakerProfile>.Filter.Eq(p => p.IsProvisional, true),
            Builders<SpeakerProfile>.Filter.Lt(p => p.LastSeenAt, cutoff));

        return await _collection.Find(filter).ToListAsync(ct).ConfigureAwait(false);
    }

    private void EnsureIndexes()
    {
        try
        {
            _collection.Indexes.CreateMany([
                new CreateIndexModel<SpeakerProfile>(
                    Builders<SpeakerProfile>.IndexKeys.Ascending(p => p.IsProvisional),
                    new CreateIndexOptions { Name = "idx_isProvisional" }),
                new CreateIndexModel<SpeakerProfile>(
                    Builders<SpeakerProfile>.IndexKeys.Ascending(p => p.LastSeenAt),
                    new CreateIndexOptions { Name = "idx_lastSeenAt" }),
                new CreateIndexModel<SpeakerProfile>(
                    Builders<SpeakerProfile>.IndexKeys
                        .Ascending(p => p.IsProvisional)
                        .Ascending(p => p.LastSeenAt),
                    new CreateIndexOptions { Name = "idx_provisional_lastSeen" }),
            ]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create speaker profile indexes — they may already exist");
        }
    }
}
