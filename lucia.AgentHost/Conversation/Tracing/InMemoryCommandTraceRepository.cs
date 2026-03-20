using System.Collections.Concurrent;

using lucia.Agents.Training.Models;

using lucia.Agents.CommandTracing;

namespace lucia.AgentHost.Conversation.Tracing;

/// <summary>
/// In-memory ring buffer implementation of <see cref="ICommandTraceRepository"/>.
/// Keeps the most recent <see cref="MaxCapacity"/> traces with FIFO eviction.
/// </summary>
public sealed class InMemoryCommandTraceRepository : ICommandTraceRepository
{
    public const int MaxCapacity = 500;

    private readonly LinkedList<CommandTrace> _traces = new();
    private readonly ConcurrentDictionary<string, CommandTrace> _index = new();
    private readonly Lock _lock = new();

    public Task SaveAsync(CommandTrace trace, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _traces.AddFirst(trace);
            _index[trace.Id] = trace;

            while (_traces.Count > MaxCapacity)
            {
                var oldest = _traces.Last!.Value;
                _traces.RemoveLast();
                _index.TryRemove(oldest.Id, out _);
            }
        }

        return Task.CompletedTask;
    }

    public Task<CommandTrace?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        _index.TryGetValue(id, out var trace);
        return Task.FromResult(trace);
    }

    public Task<PagedResult<CommandTrace>> ListAsync(CommandTraceFilter filter, CancellationToken ct = default)
    {
        IEnumerable<CommandTrace> query;
        lock (_lock)
        {
            query = _traces.ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search;
            query = query.Where(t =>
                t.RawText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                t.CleanText.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Outcome is not null)
            query = query.Where(t => t.Outcome == filter.Outcome.Value);

        if (!string.IsNullOrWhiteSpace(filter.SkillId))
            query = query.Where(t => t.Match.SkillId == filter.SkillId);

        if (filter.FromDate is not null)
            query = query.Where(t => t.Timestamp >= filter.FromDate.Value);

        if (filter.ToDate is not null)
            query = query.Where(t => t.Timestamp <= filter.ToDate.Value.AddDays(1));

        var items = query.ToList();
        var totalCount = items.Count;
        var pageSize = Math.Max(1, filter.PageSize);
        var page = Math.Max(1, filter.Page);

        var paged = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult(new PagedResult<CommandTrace>
        {
            Items = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        });
    }

    public Task<CommandTraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        List<CommandTrace> snapshot;
        lock (_lock)
        {
            snapshot = _traces.ToList();
        }

        var bySkill = snapshot
            .Where(t => t.Match.SkillId is not null)
            .GroupBy(t => t.Match.SkillId!)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var stats = new CommandTraceStats
        {
            TotalCount = snapshot.Count,
            CommandHandledCount = snapshot.Count(t => t.Outcome == CommandTraceOutcome.CommandHandled),
            LlmFallbackCount = snapshot.Count(t => t.Outcome is CommandTraceOutcome.LlmFallback or CommandTraceOutcome.LlmCompleted),
            ErrorCount = snapshot.Count(t => t.Outcome == CommandTraceOutcome.Error),
            AvgDurationMs = snapshot.Count > 0
                ? Math.Round(snapshot.Average(t => t.TotalDurationMs), 2)
                : 0,
            BySkill = bySkill,
        };

        return Task.FromResult(stats);
    }
}
