using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using lucia.Wyoming.Diarization;

namespace lucia.Wyoming.Telemetry;

/// <summary>
/// MongoDB-backed persistent transcript store.
/// Falls back to <see cref="InMemoryTranscriptStore"/> when no MongoDB connection is configured.
/// </summary>
public sealed class MongoTranscriptStore : ITranscriptStore
{
    private const string CollectionName = "voice_transcripts";

    private readonly IMongoCollection<TranscriptRecord> _collection;
    private readonly ILogger<MongoTranscriptStore> _logger;

    static MongoTranscriptStore()
    {
        if (!BsonClassMap.IsClassMapRegistered(typeof(TranscriptRecord)))
        {
            BsonClassMap.RegisterClassMap<TranscriptRecord>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
            });
        }

        if (!BsonClassMap.IsClassMapRegistered(typeof(PipelineStageTiming)))
        {
            BsonClassMap.RegisterClassMap<PipelineStageTiming>(cm =>
            {
                cm.AutoMap();
                cm.SetIgnoreExtraElements(true);
            });
        }
    }

    public MongoTranscriptStore(
        IMongoClient mongoClient,
        IOptions<DiarizationOptions> options,
        ILogger<MongoTranscriptStore> logger)
    {
        var db = mongoClient.GetDatabase(options.Value.ProfileStoreDatabaseName);
        _collection = db.GetCollection<TranscriptRecord>(CollectionName);
        _logger = logger;

        EnsureIndexes();
    }

    public async Task SaveAsync(TranscriptRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);

        await _collection.InsertOneAsync(record, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<TranscriptRecord?> GetAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var filter = Builders<TranscriptRecord>.Filter.Eq(r => r.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptRecord>> QueryAsync(
        string? sessionId,
        DateTimeOffset? since,
        string? speakerId,
        int limit,
        CancellationToken ct)
    {
        var filters = new List<FilterDefinition<TranscriptRecord>>();

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            filters.Add(Builders<TranscriptRecord>.Filter.Eq(r => r.SessionId, sessionId));
        }

        if (since.HasValue)
        {
            filters.Add(Builders<TranscriptRecord>.Filter.Gte(r => r.Timestamp, since.Value));
        }

        if (!string.IsNullOrWhiteSpace(speakerId))
        {
            filters.Add(Builders<TranscriptRecord>.Filter.Eq(r => r.SpeakerId, speakerId));
        }

        var filter = filters.Count > 0
            ? Builders<TranscriptRecord>.Filter.And(filters)
            : Builders<TranscriptRecord>.Filter.Empty;

        return await _collection
            .Find(filter)
            .SortByDescending(r => r.Timestamp)
            .Limit(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptRecord>> GetRecentAsync(int limit, CancellationToken ct)
    {
        return await _collection
            .Find(Builders<TranscriptRecord>.Filter.Empty)
            .SortByDescending(r => r.Timestamp)
            .Limit(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private void EnsureIndexes()
    {
        try
        {
            _collection.Indexes.CreateMany([
                new CreateIndexModel<TranscriptRecord>(
                    Builders<TranscriptRecord>.IndexKeys.Ascending(r => r.SessionId),
                    new CreateIndexOptions { Name = "idx_sessionId" }),
                new CreateIndexModel<TranscriptRecord>(
                    Builders<TranscriptRecord>.IndexKeys.Descending(r => r.Timestamp),
                    new CreateIndexOptions { Name = "idx_timestamp" }),
                new CreateIndexModel<TranscriptRecord>(
                    Builders<TranscriptRecord>.IndexKeys.Ascending(r => r.SpeakerId),
                    new CreateIndexOptions { Name = "idx_speakerId" }),
            ]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create voice transcript indexes — they may already exist");
        }
    }
}
