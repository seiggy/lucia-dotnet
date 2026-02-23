using System.Text.RegularExpressions;
using A2A;
using lucia.Agents.Training.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace lucia.Agents.Services;

/// <summary>
/// MongoDB-backed durable archive for completed agent tasks.
/// </summary>
public sealed class MongoTaskArchiveStore : ITaskArchiveStore
{
    private readonly IMongoCollection<ArchivedTask> _tasks;

    public MongoTaskArchiveStore(IMongoClient mongoClient, IOptions<TaskArchiveOptions> options)
    {
        var db = mongoClient.GetDatabase(options.Value.DatabaseName);
        _tasks = db.GetCollection<ArchivedTask>(options.Value.CollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _tasks.Indexes.CreateMany([
            new CreateIndexModel<ArchivedTask>(
                Builders<ArchivedTask>.IndexKeys.Descending(t => t.ArchivedAt)),
            new CreateIndexModel<ArchivedTask>(
                Builders<ArchivedTask>.IndexKeys.Ascending(t => t.Status)),
            new CreateIndexModel<ArchivedTask>(
                Builders<ArchivedTask>.IndexKeys.Ascending("AgentIds")),
        ]);
    }

    public async Task ArchiveTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        var archived = MapToArchived(task);
        var filter = Builders<ArchivedTask>.Filter.Eq(t => t.Id, archived.Id);
        await _tasks.ReplaceOneAsync(filter, archived, new ReplaceOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArchivedTask?> GetArchivedTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ArchivedTask>.Filter.Eq(t => t.Id, taskId);
        return await _tasks.Find(filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<ArchivedTask>> ListArchivedTasksAsync(TaskFilterCriteria filter, CancellationToken cancellationToken = default)
    {
        var mongoFilter = BuildFilter(filter);
        var sort = Builders<ArchivedTask>.Sort.Descending(t => t.ArchivedAt);

        var totalCount = await _tasks.CountDocumentsAsync(mongoFilter, cancellationToken: cancellationToken).ConfigureAwait(false);
        var skip = (filter.Page - 1) * filter.PageSize;

        var items = await _tasks.Find(mongoFilter)
            .Sort(sort)
            .Skip(skip)
            .Limit(filter.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<ArchivedTask>
        {
            Items = items,
            TotalCount = (int)totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task<TaskStats> GetTaskStatsAsync(CancellationToken cancellationToken = default)
    {
        var fb = Builders<ArchivedTask>.Filter;

        var totalTask = _tasks.CountDocumentsAsync(fb.Empty, cancellationToken: cancellationToken);
        var completedTask = _tasks.CountDocumentsAsync(fb.Eq(t => t.Status, "Completed"), cancellationToken: cancellationToken);
        var failedTask = _tasks.CountDocumentsAsync(fb.Eq(t => t.Status, "Failed"), cancellationToken: cancellationToken);
        var canceledTask = _tasks.CountDocumentsAsync(fb.Eq(t => t.Status, "Canceled"), cancellationToken: cancellationToken);

        await Task.WhenAll(totalTask, completedTask, failedTask, canceledTask).ConfigureAwait(false);

        // Aggregate by agent
        var agentPipeline = new BsonDocument[]
        {
            new("$unwind", "$AgentIds"),
            new("$group", new BsonDocument
            {
                { "_id", "$AgentIds" },
                { "Count", new BsonDocument("$sum", 1) }
            })
        };

        var agentGroups = await _tasks
            .Aggregate<BsonDocument>(agentPipeline)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

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

        return new TaskStats
        {
            TotalTasks = (int)totalTask.Result,
            CompletedCount = (int)completedTask.Result,
            FailedCount = (int)failedTask.Result,
            CanceledCount = (int)canceledTask.Result,
            ByAgent = byAgent,
        };
    }

    public async Task<bool> IsArchivedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ArchivedTask>.Filter.Eq(t => t.Id, taskId);
        return await _tasks.CountDocumentsAsync(filter, cancellationToken: cancellationToken).ConfigureAwait(false) > 0;
    }

    private static ArchivedTask MapToArchived(AgentTask task)
    {
        var history = task.History ?? [];
        var agentIds = history
            .Where(m => m.Role == MessageRole.Agent)
            .Select(m => m.Extensions?.FirstOrDefault() ?? "unknown")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var userInput = history
            .Where(m => m.Role == MessageRole.User)
            .SelectMany(m => m.Parts?.OfType<TextPart>() ?? [])
            .FirstOrDefault()?.Text;

        var finalResponse = history
            .Where(m => m.Role == MessageRole.Agent)
            .SelectMany(m => m.Parts?.OfType<TextPart>() ?? [])
            .LastOrDefault()?.Text;

        var messages = history.Select(m => new ArchivedMessage
        {
            Role = m.Role.ToString(),
            Text = string.Join(' ', m.Parts?.OfType<TextPart>().Select(p => p.Text) ?? []),
            MessageId = m.MessageId,
        }).ToList();

        return new ArchivedTask
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = task.Status.State.ToString(),
            AgentIds = agentIds,
            UserInput = userInput,
            FinalResponse = finalResponse,
            MessageCount = history.Count,
            History = messages,
            CreatedAt = task.Status.Timestamp.UtcDateTime,
            ArchivedAt = DateTime.UtcNow,
        };
    }

    private static FilterDefinition<ArchivedTask> BuildFilter(TaskFilterCriteria criteria)
    {
        var fb = Builders<ArchivedTask>.Filter;
        var filters = new List<FilterDefinition<ArchivedTask>>();

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            filters.Add(fb.Eq(t => t.Status, criteria.Status));
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentId))
        {
            filters.Add(fb.AnyEq(t => t.AgentIds, criteria.AgentId));
        }

        if (criteria.FromDate.HasValue)
        {
            filters.Add(fb.Gte(t => t.ArchivedAt, criteria.FromDate.Value));
        }

        if (criteria.ToDate.HasValue)
        {
            filters.Add(fb.Lte(t => t.ArchivedAt, criteria.ToDate.Value));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var regex = new Regex(Regex.Escape(criteria.Search), RegexOptions.IgnoreCase);
            filters.Add(fb.Regex(t => t.UserInput, regex.ToString()));
        }

        return filters.Count > 0 ? fb.And(filters) : fb.Empty;
    }
}
