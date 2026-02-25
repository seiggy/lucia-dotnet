using MongoDB.Driver;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// MongoDB-backed repository for scheduled task persistence.
/// Uses the <c>luciatasks</c> database, <c>scheduled_tasks</c> collection.
/// </summary>
public sealed class MongoScheduledTaskRepository : IScheduledTaskRepository
{
    private const string DatabaseName = "luciatasks";
    private const string CollectionName = "scheduled_tasks";

    private readonly IMongoCollection<ScheduledTaskDocument> _collection;

    public MongoScheduledTaskRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(DatabaseName);
        _collection = db.GetCollection<ScheduledTaskDocument>(CollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _collection.Indexes.CreateMany([
            new CreateIndexModel<ScheduledTaskDocument>(
                Builders<ScheduledTaskDocument>.IndexKeys.Ascending(d => d.Status)),
            new CreateIndexModel<ScheduledTaskDocument>(
                Builders<ScheduledTaskDocument>.IndexKeys.Ascending(d => d.FireAt)),
            new CreateIndexModel<ScheduledTaskDocument>(
                Builders<ScheduledTaskDocument>.IndexKeys.Ascending(d => d.TaskType)),
        ]);
    }

    public async Task UpsertAsync(ScheduledTaskDocument document, CancellationToken ct = default)
    {
        var filter = Builders<ScheduledTaskDocument>.Filter.Eq(d => d.Id, document.Id);
        await _collection.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true }, ct)
            .ConfigureAwait(false);
    }

    public async Task<ScheduledTaskDocument?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<ScheduledTaskDocument>.Filter.Eq(d => d.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScheduledTaskDocument>> GetRecoverableTasksAsync(CancellationToken ct = default)
    {
        var filter = Builders<ScheduledTaskDocument>.Filter.In(
            d => d.Status,
            new[] { ScheduledTaskStatus.Pending, ScheduledTaskStatus.Active });

        return await _collection.Find(filter).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(string id, ScheduledTaskStatus status, CancellationToken ct = default)
    {
        var filter = Builders<ScheduledTaskDocument>.Filter.Eq(d => d.Id, id);
        var update = Builders<ScheduledTaskDocument>.Update.Set(d => d.Status, status);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<ScheduledTaskDocument>.Filter.Eq(d => d.Id, id);
        await _collection.DeleteOneAsync(filter, ct).ConfigureAwait(false);
    }

    public async Task<long> PurgeCompletedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var filter = Builders<ScheduledTaskDocument>.Filter.And(
            Builders<ScheduledTaskDocument>.Filter.In(
                d => d.Status,
                new[]
                {
                    ScheduledTaskStatus.Completed,
                    ScheduledTaskStatus.Dismissed,
                    ScheduledTaskStatus.AutoDismissed,
                    ScheduledTaskStatus.Cancelled,
                    ScheduledTaskStatus.Failed
                }),
            Builders<ScheduledTaskDocument>.Filter.Lt(d => d.FireAt, cutoff));

        var result = await _collection.DeleteManyAsync(filter, ct).ConfigureAwait(false);
        return result.DeletedCount;
    }
}
