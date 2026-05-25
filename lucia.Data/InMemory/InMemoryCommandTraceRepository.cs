using System.Linq;

using lucia.Agents.CommandTracing;
using lucia.Agents.Training.Models;

namespace lucia.Data.InMemory;

/// <summary>
/// In-memory fallback implementation of <see cref="ICommandTraceRepository"/>.
/// Keeps a bounded FIFO history so PostgreSQL mode can run without SQLite dependencies.
/// </summary>
public sealed class InMemoryCommandTraceRepository : ICommandTraceRepository
{
    public const int MaxCapacity = 500;

    private readonly LinkedList<CommandTrace> _traces = new();
    private readonly Dictionary<string, CommandTrace> _index = new(StringComparer.OrdinalIgnoreCase);
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
                _index.Remove(oldest.Id);
            }
        }

        return Task.CompletedTask;
    }

    public Task<CommandTrace?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _index.TryGetValue(id, out var trace);
            return Task.FromResult(trace);
        }
    }

    public Task<PagedResult<CommandTrace>> ListAsync(CommandTraceFilter filter, CancellationToken ct = default)
    {
        List<CommandTrace> snapshot;
        lock (_lock)
        {
            snapshot = _traces.ToList();
        }

        var query = snapshot.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            query = query.Where(trace =>
                trace.RawText.Contains(filter.Search, StringComparison.OrdinalIgnoreCase) ||
                trace.CleanText.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Outcome is not null)
        {
            query = query.Where(trace => trace.Outcome == filter.Outcome.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SkillId))
        {
            query = query.Where(trace => string.Equals(trace.Match.SkillId, filter.SkillId, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.FromDate is not null)
        {
            query = query.Where(trace => trace.Timestamp >= filter.FromDate.Value);
        }

        if (filter.ToDate is not null)
        {
            query = query.Where(trace => trace.Timestamp <= filter.ToDate.Value.AddDays(1));
        }

        var items = query.ToList();
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Max(1, filter.PageSize);

        return Task.FromResult(new PagedResult<CommandTrace>
        {
            Items = items
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            TotalCount = items.Count,
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
            .Where(trace => !string.IsNullOrWhiteSpace(trace.Match.SkillId))
            .GroupBy(trace => trace.Match.SkillId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.OrdinalIgnoreCase);

        var averageDuration = snapshot.Count > 0
            ? Math.Round(snapshot.Average(trace => trace.TotalDurationMs), 2)
            : 0;

        return Task.FromResult(new CommandTraceStats
        {
            TotalCount = snapshot.Count,
            CommandHandledCount = snapshot.Count(trace => trace.Outcome == CommandTraceOutcome.CommandHandled),
            LlmFallbackCount = snapshot.Count(trace => trace.Outcome is CommandTraceOutcome.LlmFallback or CommandTraceOutcome.LlmCompleted),
            ErrorCount = snapshot.Count(trace => trace.Outcome == CommandTraceOutcome.Error),
            AvgDurationMs = averageDuration,
            BySkill = bySkill,
        });
    }
}
