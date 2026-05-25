using System.Linq;

using lucia.Agents.Training;
using lucia.Agents.Training.Models;

namespace lucia.Data.InMemory;

/// <summary>
/// In-memory fallback implementation of <see cref="ITraceRepository"/> used when the selected
/// store does not yet provide durable trace persistence.
/// </summary>
public sealed class InMemoryTraceRepository : ITraceRepository
{
    private readonly Dictionary<string, ConversationTrace> _traces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DatasetExportRecord> _exports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public Task InsertTraceAsync(ConversationTrace trace, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _traces[trace.Id] = trace;
        }

        return Task.CompletedTask;
    }

    public Task<ConversationTrace?> GetTraceAsync(string traceId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _traces.TryGetValue(traceId, out var trace);
            return Task.FromResult(trace);
        }
    }

    public Task<List<ConversationTrace>> GetTracesBySessionIdAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var traces = _traces.Values
                .Where(trace => string.Equals(trace.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(trace => trace.Timestamp)
                .ToList();

            return Task.FromResult(traces);
        }
    }

    public Task<PagedResult<ConversationTrace>> ListTracesAsync(TraceFilterCriteria filter, CancellationToken ct = default)
    {
        List<ConversationTrace> snapshot;
        lock (_lock)
        {
            snapshot = _traces.Values.ToList();
        }

        var filtered = snapshot.AsEnumerable();

        if (filter.FromDate.HasValue)
        {
            filtered = filtered.Where(trace => trace.Timestamp >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            filtered = filtered.Where(trace => trace.Timestamp <= filter.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.AgentFilter))
        {
            filtered = filtered.Where(trace => trace.AgentExecutions.Any(execution =>
                string.Equals(execution.AgentId, filter.AgentFilter, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(filter.ModelFilter))
        {
            filtered = filtered.Where(trace => trace.AgentExecutions.Any(execution =>
                string.Equals(execution.ModelDeploymentName, filter.ModelFilter, StringComparison.OrdinalIgnoreCase)));
        }

        if (filter.LabelFilter.HasValue)
        {
            filtered = filtered.Where(trace => trace.Label.Status == filter.LabelFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            filtered = filtered.Where(trace => trace.UserInput.Contains(filter.SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderByDescending(trace => trace.Timestamp)
            .ToList();
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Max(1, filter.PageSize);

        return Task.FromResult(new PagedResult<ConversationTrace>
        {
            Items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            TotalCount = ordered.Count,
            Page = page,
            PageSize = pageSize,
        });
    }

    public Task UpdateLabelAsync(string traceId, TraceLabel label, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_traces.TryGetValue(traceId, out var trace))
            {
                trace.Label = label;
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteTraceAsync(string traceId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _traces.Remove(traceId);
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteOldUnlabeledAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = 0;

        lock (_lock)
        {
            var traceIds = _traces.Values
                .Where(trace => trace.Label.Status == LabelStatus.Unlabeled && trace.Timestamp < cutoff)
                .Select(trace => trace.Id)
                .ToList();

            foreach (var traceId in traceIds)
            {
                if (_traces.Remove(traceId))
                {
                    deleted++;
                }
            }
        }

        return Task.FromResult(deleted);
    }

    public Task InsertExportRecordAsync(DatasetExportRecord export, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _exports[export.Id] = export;
        }

        return Task.CompletedTask;
    }

    public Task<DatasetExportRecord?> GetExportRecordAsync(string exportId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _exports.TryGetValue(exportId, out var export);
            return Task.FromResult(export);
        }
    }

    public Task<List<DatasetExportRecord>> ListExportRecordsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var exports = _exports.Values
                .OrderByDescending(export => export.Timestamp)
                .ToList();

            return Task.FromResult(exports);
        }
    }

    public Task<List<ConversationTrace>> GetTracesForExportAsync(ExportFilterCriteria filter, CancellationToken ct = default)
    {
        List<ConversationTrace> snapshot;
        lock (_lock)
        {
            snapshot = _traces.Values.ToList();
        }

        var filtered = snapshot.AsEnumerable();

        if (filter.LabelFilter.HasValue)
        {
            filtered = filtered.Where(trace => trace.Label.Status == filter.LabelFilter.Value);
        }

        if (filter.FromDate.HasValue)
        {
            filtered = filtered.Where(trace => trace.Timestamp >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            filtered = filtered.Where(trace => trace.Timestamp <= filter.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.AgentFilter))
        {
            filtered = filtered.Where(trace => trace.AgentExecutions.Any(execution =>
                string.Equals(execution.AgentId, filter.AgentFilter, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(filter.ModelFilter))
        {
            filtered = filtered.Where(trace => trace.AgentExecutions.Any(execution =>
                string.Equals(execution.ModelDeploymentName, filter.ModelFilter, StringComparison.OrdinalIgnoreCase)));
        }

        return Task.FromResult(filtered
            .OrderByDescending(trace => trace.Timestamp)
            .ToList());
    }

    public Task<TraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        List<ConversationTrace> snapshot;
        lock (_lock)
        {
            snapshot = _traces.Values.ToList();
        }

        var byAgent = snapshot
            .SelectMany(trace => trace.AgentExecutions)
            .Where(execution => !string.IsNullOrWhiteSpace(execution.AgentId))
            .GroupBy(execution => execution.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var errorsByAgent = snapshot
            .SelectMany(trace => trace.AgentExecutions)
            .Where(execution => !execution.Success && !string.IsNullOrWhiteSpace(execution.AgentId))
            .GroupBy(execution => execution.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new TraceStats
        {
            TotalTraces = snapshot.Count,
            UnlabeledCount = snapshot.Count(trace => trace.Label.Status == LabelStatus.Unlabeled),
            PositiveCount = snapshot.Count(trace => trace.Label.Status == LabelStatus.Positive),
            NegativeCount = snapshot.Count(trace => trace.Label.Status == LabelStatus.Negative),
            ErroredCount = snapshot.Count(trace => trace.IsErrored),
            ByAgent = byAgent,
            ErrorsByAgent = errorsByAgent,
        });
    }
}
