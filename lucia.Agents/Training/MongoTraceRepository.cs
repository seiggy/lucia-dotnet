using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using lucia.Agents.Training.Models;

namespace lucia.Agents.Training;

/// <summary>
/// MongoDB implementation of <see cref="ITraceRepository"/>.
/// </summary>
public sealed class MongoTraceRepository : ITraceRepository
{
    private readonly IMongoCollection<ConversationTrace> _traces;
    private readonly IMongoCollection<DatasetExportRecord> _exports;

    public MongoTraceRepository(IMongoClient mongoClient, IOptions<TraceCaptureOptions> options)
    {
        var db = mongoClient.GetDatabase(options.Value.DatabaseName);
        _traces = db.GetCollection<ConversationTrace>(options.Value.TracesCollectionName);
        _exports = db.GetCollection<DatasetExportRecord>(options.Value.ExportsCollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _traces.Indexes.CreateMany([
            new CreateIndexModel<ConversationTrace>(
                Builders<ConversationTrace>.IndexKeys.Descending(t => t.Timestamp)),
            new CreateIndexModel<ConversationTrace>(
                Builders<ConversationTrace>.IndexKeys.Ascending("Label.Status")),
            new CreateIndexModel<ConversationTrace>(
                Builders<ConversationTrace>.IndexKeys.Ascending("AgentExecutions.AgentId")),
            new CreateIndexModel<ConversationTrace>(
                Builders<ConversationTrace>.IndexKeys.Ascending(t => t.SessionId)),
        ]);
    }

    public async Task InsertTraceAsync(ConversationTrace trace, CancellationToken ct = default)
    {
        await _traces.InsertOneAsync(trace, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<ConversationTrace?> GetTraceAsync(string traceId, CancellationToken ct = default)
    {
        var filter = Builders<ConversationTrace>.Filter.Eq(t => t.Id, traceId);
        return await _traces.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ConversationTrace>> GetTracesBySessionIdAsync(string sessionId, CancellationToken ct = default)
    {
        var filter = Builders<ConversationTrace>.Filter.Eq(t => t.SessionId, sessionId);
        var sort = Builders<ConversationTrace>.Sort.Ascending(t => t.Timestamp);
        return await _traces.Find(filter).Sort(sort).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<ConversationTrace>> ListTracesAsync(TraceFilterCriteria filter, CancellationToken ct = default)
    {
        var mongoFilter = BuildTraceFilter(filter);
        var sort = Builders<ConversationTrace>.Sort.Descending(t => t.Timestamp);

        var totalCount = await _traces.CountDocumentsAsync(mongoFilter, cancellationToken: ct).ConfigureAwait(false);
        var skip = (filter.Page - 1) * filter.PageSize;

        var items = await _traces.Find(mongoFilter)
            .Sort(sort)
            .Skip(skip)
            .Limit(filter.PageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return new PagedResult<ConversationTrace>
        {
            Items = items,
            TotalCount = (int)totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task UpdateLabelAsync(string traceId, TraceLabel label, CancellationToken ct = default)
    {
        var filter = Builders<ConversationTrace>.Filter.Eq(t => t.Id, traceId);
        var update = Builders<ConversationTrace>.Update.Set(t => t.Label, label);
        await _traces.UpdateOneAsync(filter, update, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task DeleteTraceAsync(string traceId, CancellationToken ct = default)
    {
        var filter = Builders<ConversationTrace>.Filter.Eq(t => t.Id, traceId);
        await _traces.DeleteOneAsync(filter, ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteOldUnlabeledAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var filter = Builders<ConversationTrace>.Filter.And(
            Builders<ConversationTrace>.Filter.Eq("Label.Status", LabelStatus.Unlabeled),
            Builders<ConversationTrace>.Filter.Lt(t => t.Timestamp, cutoff));

        var result = await _traces.DeleteManyAsync(filter, ct).ConfigureAwait(false);
        return (int)result.DeletedCount;
    }

    public async Task InsertExportRecordAsync(DatasetExportRecord export, CancellationToken ct = default)
    {
        await _exports.InsertOneAsync(export, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<DatasetExportRecord?> GetExportRecordAsync(string exportId, CancellationToken ct = default)
    {
        var filter = Builders<DatasetExportRecord>.Filter.Eq(e => e.Id, exportId);
        return await _exports.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<DatasetExportRecord>> ListExportRecordsAsync(CancellationToken ct = default)
    {
        var sort = Builders<DatasetExportRecord>.Sort.Descending(e => e.Timestamp);
        return await _exports.Find(Builders<DatasetExportRecord>.Filter.Empty)
            .Sort(sort)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<ConversationTrace>> GetTracesForExportAsync(ExportFilterCriteria filter, CancellationToken ct = default)
    {
        var mongoFilter = BuildExportFilter(filter);
        var sort = Builders<ConversationTrace>.Sort.Descending(t => t.Timestamp);
        return await _traces.Find(mongoFilter).Sort(sort).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<TraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        var fb = Builders<ConversationTrace>.Filter;

        var totalTask = _traces.CountDocumentsAsync(fb.Empty, cancellationToken: ct);
        var unlabeledTask = _traces.CountDocumentsAsync(
            fb.Eq("Label.Status", LabelStatus.Unlabeled), cancellationToken: ct);
        var positiveTask = _traces.CountDocumentsAsync(
            fb.Eq("Label.Status", LabelStatus.Positive), cancellationToken: ct);
        var negativeTask = _traces.CountDocumentsAsync(
            fb.Eq("Label.Status", LabelStatus.Negative), cancellationToken: ct);
        var erroredTask = _traces.CountDocumentsAsync(
            fb.Eq(t => t.IsErrored, true), cancellationToken: ct);

        await Task.WhenAll(totalTask, unlabeledTask, positiveTask, negativeTask, erroredTask).ConfigureAwait(false);

        // Aggregate traces by agent — after Unwind, AgentExecutions is a single object
        var agentPipeline = new MongoDB.Bson.BsonDocument[]
        {
            new("$unwind", "$AgentExecutions"),
            new("$group", new MongoDB.Bson.BsonDocument
            {
                { "_id", "$AgentExecutions.AgentId" },
                { "Count", new MongoDB.Bson.BsonDocument("$sum", 1) }
            })
        };

        var agentGroups = await _traces
            .Aggregate<MongoDB.Bson.BsonDocument>(agentPipeline)
            .ToListAsync(ct).ConfigureAwait(false);

        var byAgent = new Dictionary<string, int>();
        foreach (var group in agentGroups)
        {
            var agentId = group["_id"].AsString;
            var count = group["Count"].AsInt32;
            if (!string.IsNullOrEmpty(agentId))
            {
                byAgent[agentId] = count;
            }
        }

        // Aggregate per-agent errors (where AgentExecutions.Success == false)
        var errorPipeline = new MongoDB.Bson.BsonDocument[]
        {
            new("$unwind", "$AgentExecutions"),
            new("$match", new MongoDB.Bson.BsonDocument("AgentExecutions.Success", false)),
            new("$group", new MongoDB.Bson.BsonDocument
            {
                { "_id", "$AgentExecutions.AgentId" },
                { "Count", new MongoDB.Bson.BsonDocument("$sum", 1) }
            })
        };

        var errorGroups = await _traces
            .Aggregate<MongoDB.Bson.BsonDocument>(errorPipeline)
            .ToListAsync(ct).ConfigureAwait(false);

        var errorsByAgent = new Dictionary<string, int>();
        foreach (var group in errorGroups)
        {
            var agentId = group["_id"].AsString;
            var count = group["Count"].AsInt32;
            if (!string.IsNullOrEmpty(agentId))
            {
                errorsByAgent[agentId] = count;
            }
        }

        return new TraceStats
        {
            TotalTraces = (int)totalTask.Result,
            UnlabeledCount = (int)unlabeledTask.Result,
            PositiveCount = (int)positiveTask.Result,
            NegativeCount = (int)negativeTask.Result,
            ErroredCount = (int)erroredTask.Result,
            ByAgent = byAgent,
            ErrorsByAgent = errorsByAgent,
        };
    }

    private static FilterDefinition<ConversationTrace> BuildTraceFilter(TraceFilterCriteria criteria)
    {
        var fb = Builders<ConversationTrace>.Filter;
        var filters = new List<FilterDefinition<ConversationTrace>>();

        if (criteria.FromDate.HasValue)
        {
            filters.Add(fb.Gte(t => t.Timestamp, criteria.FromDate.Value));
        }

        if (criteria.ToDate.HasValue)
        {
            filters.Add(fb.Lte(t => t.Timestamp, criteria.ToDate.Value));
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentFilter))
        {
            filters.Add(fb.ElemMatch(
                t => t.AgentExecutions,
                Builders<AgentExecutionRecord>.Filter.Eq(a => a.AgentId, criteria.AgentFilter)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.ModelFilter))
        {
            filters.Add(fb.ElemMatch(
                t => t.AgentExecutions,
                Builders<AgentExecutionRecord>.Filter.Eq(a => a.ModelDeploymentName, criteria.ModelFilter)));
        }

        if (criteria.LabelFilter.HasValue)
        {
            filters.Add(fb.Eq("Label.Status", criteria.LabelFilter.Value));
        }

        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            var regex = new Regex(Regex.Escape(criteria.SearchText), RegexOptions.IgnoreCase);
            filters.Add(fb.Regex(t => t.UserInput, regex.ToString()));
        }

        return filters.Count > 0 ? fb.And(filters) : fb.Empty;
    }

    private static FilterDefinition<ConversationTrace> BuildExportFilter(ExportFilterCriteria criteria)
    {
        var fb = Builders<ConversationTrace>.Filter;
        var filters = new List<FilterDefinition<ConversationTrace>>();

        if (criteria.LabelFilter.HasValue)
        {
            filters.Add(fb.Eq("Label.Status", criteria.LabelFilter.Value));
        }

        if (criteria.FromDate.HasValue)
        {
            filters.Add(fb.Gte(t => t.Timestamp, criteria.FromDate.Value));
        }

        if (criteria.ToDate.HasValue)
        {
            filters.Add(fb.Lte(t => t.Timestamp, criteria.ToDate.Value));
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentFilter))
        {
            filters.Add(fb.ElemMatch(
                t => t.AgentExecutions,
                Builders<AgentExecutionRecord>.Filter.Eq(a => a.AgentId, criteria.AgentFilter)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.ModelFilter))
        {
            filters.Add(fb.ElemMatch(
                t => t.AgentExecutions,
                Builders<AgentExecutionRecord>.Filter.Eq(a => a.ModelDeploymentName, criteria.ModelFilter)));
        }

        return filters.Count > 0 ? fb.And(filters) : fb.Empty;
    }
}
