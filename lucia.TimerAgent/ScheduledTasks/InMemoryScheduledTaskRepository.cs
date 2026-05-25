namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// In-memory fallback implementation of <see cref="IScheduledTaskRepository"/>.
/// </summary>
public sealed class InMemoryScheduledTaskRepository : IScheduledTaskRepository
{
    private readonly Dictionary<string, ScheduledTaskDocument> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public Task UpsertAsync(ScheduledTaskDocument document, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _tasks[document.Id] = document;
        }

        return Task.CompletedTask;
    }

    public Task<ScheduledTaskDocument?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _tasks.TryGetValue(id, out var document);
            return Task.FromResult(document);
        }
    }

    public Task<IReadOnlyList<ScheduledTaskDocument>> GetRecoverableTasksAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ScheduledTaskDocument> documents = _tasks.Values
                .Where(document => document.Status is ScheduledTaskStatus.Pending or ScheduledTaskStatus.Active)
                .OrderBy(document => document.FireAt)
                .ToList();

            return Task.FromResult(documents);
        }
    }

    public Task UpdateStatusAsync(string id, ScheduledTaskStatus status, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_tasks.TryGetValue(id, out var document))
            {
                document.Status = status;
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _tasks.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task<long> PurgeCompletedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        long deleted = 0;

        lock (_lock)
        {
            var ids = _tasks.Values
                .Where(document =>
                    (document.Status is ScheduledTaskStatus.Completed or ScheduledTaskStatus.Dismissed or ScheduledTaskStatus.AutoDismissed or ScheduledTaskStatus.Cancelled or ScheduledTaskStatus.Failed)
                    && document.FireAt < cutoff)
                .Select(document => document.Id)
                .ToList();

            foreach (var id in ids)
            {
                if (_tasks.Remove(id))
                {
                    deleted++;
                }
            }
        }

        return Task.FromResult(deleted);
    }
}
