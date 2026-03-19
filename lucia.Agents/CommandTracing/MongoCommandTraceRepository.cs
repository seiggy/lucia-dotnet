using lucia.Agents.CommandTracing;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;

using Microsoft.Extensions.Options;

using MongoDB.Driver;

namespace lucia.Agents.CommandTracing;

/// <summary>
/// MongoDB implementation of <see cref="ICommandTraceRepository"/>.
/// Stores command traces in a dedicated collection within the traces database.
/// </summary>
public sealed class MongoCommandTraceRepository : ICommandTraceRepository
{
    private const string CollectionName = "command_traces";

    private readonly IMongoCollection<CommandTrace> _collection;

    public MongoCommandTraceRepository(
        IMongoClient mongoClient,
        IOptions<TraceCaptureOptions> options)
    {
        var db = mongoClient.GetDatabase(options.Value.DatabaseName);
        _collection = db.GetCollection<CommandTrace>(CollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _collection.Indexes.CreateMany(
        [
            new CreateIndexModel<CommandTrace>(
                Builders<CommandTrace>.IndexKeys.Descending(t => t.Timestamp)),
            new CreateIndexModel<CommandTrace>(
                Builders<CommandTrace>.IndexKeys.Ascending(t => t.Outcome)),
            new CreateIndexModel<CommandTrace>(
                Builders<CommandTrace>.IndexKeys.Ascending("Match.SkillId")),
        ]);
    }

    public async Task SaveAsync(CommandTrace trace, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(trace, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<CommandTrace?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<CommandTrace>.Filter.Eq(t => t.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<CommandTrace>> ListAsync(CommandTraceFilter filter, CancellationToken ct = default)
    {
        var mongoFilter = BuildFilter(filter);
        var sort = Builders<CommandTrace>.Sort.Descending(t => t.Timestamp);

        var totalCount = await _collection.CountDocumentsAsync(mongoFilter, cancellationToken: ct)
            .ConfigureAwait(false);

        var skip = (filter.Page - 1) * filter.PageSize;
        var items = await _collection.Find(mongoFilter)
            .Sort(sort)
            .Skip(skip)
            .Limit(filter.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<CommandTrace>
        {
            Items = items,
            TotalCount = (int)totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task<CommandTraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        var fb = Builders<CommandTrace>.Filter;

        var totalTask = _collection.CountDocumentsAsync(fb.Empty, cancellationToken: ct);
        var commandTask = _collection.CountDocumentsAsync(
            fb.Eq(t => t.Outcome, CommandTraceOutcome.CommandHandled), cancellationToken: ct);
        var llmTask = _collection.CountDocumentsAsync(
            fb.In(t => t.Outcome, new[] { CommandTraceOutcome.LlmFallback, CommandTraceOutcome.LlmCompleted }),
            cancellationToken: ct);
        var errorTask = _collection.CountDocumentsAsync(
            fb.Eq(t => t.Outcome, CommandTraceOutcome.Error), cancellationToken: ct);

        await Task.WhenAll(totalTask, commandTask, llmTask, errorTask).ConfigureAwait(false);

        var total = await totalTask.ConfigureAwait(false);
        var command = await commandTask.ConfigureAwait(false);
        var llm = await llmTask.ConfigureAwait(false);
        var errors = await errorTask.ConfigureAwait(false);

        // Avg duration
        double avgDuration = 0;
        if (total > 0)
        {
            var avgPipeline = new MongoDB.Bson.BsonDocument[]
            {
                new("$group", new MongoDB.Bson.BsonDocument
                {
                    { "_id", MongoDB.Bson.BsonNull.Value },
                    { "avg", new MongoDB.Bson.BsonDocument("$avg", "$TotalDurationMs") },
                }),
            };

            var avgResult = await _collection.Aggregate<MongoDB.Bson.BsonDocument>(avgPipeline)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (avgResult is not null)
                avgDuration = Math.Round(avgResult["avg"].AsDouble, 2);
        }

        // Per-skill breakdown
        var skillPipeline = new MongoDB.Bson.BsonDocument[]
        {
            new("$match", new MongoDB.Bson.BsonDocument("Match.SkillId", new MongoDB.Bson.BsonDocument("$ne", MongoDB.Bson.BsonNull.Value))),
            new("$group", new MongoDB.Bson.BsonDocument
            {
                { "_id", "$Match.SkillId" },
                { "Count", new MongoDB.Bson.BsonDocument("$sum", 1) },
            }),
        };

        var skillGroups = await _collection.Aggregate<MongoDB.Bson.BsonDocument>(skillPipeline)
            .ToListAsync(ct).ConfigureAwait(false);

        var bySkill = new Dictionary<string, long>();
        foreach (var group in skillGroups)
        {
            var skillId = group["_id"].AsString;
            bySkill[skillId] = group["Count"].AsInt32;
        }

        return new CommandTraceStats
        {
            TotalCount = total,
            CommandHandledCount = command,
            LlmFallbackCount = llm,
            ErrorCount = errors,
            AvgDurationMs = avgDuration,
            BySkill = bySkill,
        };
    }

    private static FilterDefinition<CommandTrace> BuildFilter(CommandTraceFilter filter)
    {
        var fb = Builders<CommandTrace>.Filter;
        var filters = new List<FilterDefinition<CommandTrace>>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var escaped = System.Text.RegularExpressions.Regex.Escape(filter.Search);
            var bsonRegex = new MongoDB.Bson.BsonRegularExpression(escaped, "i");
            filters.Add(fb.Regex(t => t.CleanText, bsonRegex));
        }

        if (filter.Outcome is not null)
            filters.Add(fb.Eq(t => t.Outcome, filter.Outcome.Value));

        if (!string.IsNullOrWhiteSpace(filter.SkillId))
            filters.Add(fb.Eq("Match.SkillId", filter.SkillId));

        if (filter.FromDate is not null)
            filters.Add(fb.Gte(t => t.Timestamp, filter.FromDate.Value));

        if (filter.ToDate is not null)
            filters.Add(fb.Lte(t => t.Timestamp, filter.ToDate.Value.AddDays(1)));

        return filters.Count > 0 ? fb.And(filters) : fb.Empty;
    }
}
