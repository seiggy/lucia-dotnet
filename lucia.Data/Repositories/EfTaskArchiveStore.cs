using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Training.Models;

using Microsoft.EntityFrameworkCore;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITaskArchiveStore"/>.
/// Uses client-side grouping for JSON-stored AgentIds when computing per-agent stats.
/// </summary>
public sealed class EfTaskArchiveStore(IDbContextFactory<LuciaDbContext> dbFactory) : ITaskArchiveStore
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    public async Task ArchiveTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var archived = MapToArchived(task);

        var existing = await db.ArchivedTasks
            .FindAsync([archived.Id], cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
            db.Entry(existing).CurrentValues.SetValues(archived);
        else
            db.ArchivedTasks.Add(archived);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArchivedTask?> GetArchivedTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ArchivedTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PagedResult<ArchivedTask>> ListArchivedTasksAsync(TaskFilterCriteria filter, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.ArchivedTasks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(t => t.Status == filter.Status);

        if (filter.FromDate.HasValue)
            query = query.Where(t => t.ArchivedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(t => t.ArchivedAt <= filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(t => t.UserInput != null && EF.Functions.Like(t.UserInput, $"%{filter.Search}%"));

        // AgentId filter requires client-side evaluation (JSON column)
        if (!string.IsNullOrWhiteSpace(filter.AgentId))
        {
            var allFiltered = await query
                .OrderByDescending(t => t.ArchivedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            allFiltered = allFiltered
                .Where(t => t.AgentIds.Contains(filter.AgentId, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var totalCount = allFiltered.Count;
            var skip = (filter.Page - 1) * filter.PageSize;
            var items = allFiltered.Skip(skip).Take(filter.PageSize).ToList();

            return new PagedResult<ArchivedTask>
            {
                Items = items,
                TotalCount = totalCount,
                Page = filter.Page,
                PageSize = filter.PageSize,
            };
        }

        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageSkip = (filter.Page - 1) * filter.PageSize;

        var pageItems = await query
            .OrderByDescending(t => t.ArchivedAt)
            .Skip(pageSkip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ArchivedTask>
        {
            Items = pageItems,
            TotalCount = count,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task<TaskStats> GetTaskStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var total = await db.ArchivedTasks.CountAsync(cancellationToken).ConfigureAwait(false);
        var completed = await db.ArchivedTasks.CountAsync(t => t.Status == "Completed", cancellationToken).ConfigureAwait(false);
        var failed = await db.ArchivedTasks.CountAsync(t => t.Status == "Failed", cancellationToken).ConfigureAwait(false);
        var canceled = await db.ArchivedTasks.CountAsync(t => t.Status == "Canceled", cancellationToken).ConfigureAwait(false);

        // AgentIds is a JSON column — group in memory
        var agentIdLists = await db.ArchivedTasks
            .AsNoTracking()
            .Select(t => t.AgentIds)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byAgent = agentIdLists
            .SelectMany(ids => ids)
            .Where(id => !string.IsNullOrEmpty(id))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        return new TaskStats
        {
            TotalTasks = total,
            CompletedCount = completed,
            FailedCount = failed,
            CanceledCount = canceled,
            ByAgent = byAgent,
        };
    }

    public async Task<bool> IsArchivedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ArchivedTasks
            .AnyAsync(t => t.Id == taskId, cancellationToken)
            .ConfigureAwait(false);
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
}
