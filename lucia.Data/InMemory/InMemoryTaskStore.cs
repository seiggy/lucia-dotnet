using System.Collections.Concurrent;
using A2A;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Data.InMemory;

/// <summary>
/// In-memory implementation of <see cref="ITaskStore"/> and <see cref="ITaskIdIndex"/>.
/// Replaces the Redis-backed version for lightweight/mono-container deployments.
/// Tasks are stored with a 24h TTL and cleaned up periodically.
/// </summary>
public sealed class InMemoryTaskStore : ITaskStore, ITaskIdIndex, IDisposable
{
    private static readonly TimeSpan DefaultTaskTtl = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, TimestampedEntry<AgentTask>> _tasks = new();
    private readonly ConcurrentDictionary<string, byte> _taskIdIndex = new();

    private readonly ILogger<InMemoryTaskStore> _logger;
    private readonly Timer _cleanupTimer;

    public InMemoryTaskStore(ILogger<InMemoryTaskStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Periodic TTL sweep every 5 minutes
        _cleanupTimer = new Timer(EvictExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_tasks.TryGetValue(taskId, out var entry) && !entry.IsExpired)
            return Task.FromResult<AgentTask?>(entry.Value);

        if (entry is not null)
        {
            _tasks.TryRemove(taskId, out _);
            _taskIdIndex.TryRemove(taskId, out _);
        }

        return Task.FromResult<AgentTask?>(null);
    }

    public Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        _tasks[taskId] = new TimestampedEntry<AgentTask>(task, DefaultTaskTtl);
        _taskIdIndex[taskId] = 0;

        return Task.CompletedTask;
    }

    public Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryRemove(taskId, out _);
        _taskIdIndex.TryRemove(taskId, out _);
        return Task.CompletedTask;
    }

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        var tasks = _tasks.Values
            .Where(e => !e.IsExpired)
            .Select(e => e.Value)
            .ToList();

        return Task.FromResult(new ListTasksResponse { Tasks = tasks });
    }

    // ── ITaskIdIndex ────────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> GetAllTrackedTaskIdsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> ids = _taskIdIndex.Keys.ToList();
        return Task.FromResult(ids);
    }

    public Task RemoveTaskIdAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _taskIdIndex.TryRemove(taskId, out _);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    // ── TTL cleanup ─────────────────────────────────────────────────────

    private void EvictExpiredEntries(object? state)
    {
        var tasksRemoved = 0;
        foreach (var kvp in _tasks)
        {
            if (kvp.Value.IsExpired)
            {
                if (_tasks.TryRemove(kvp.Key, out _))
                {
                    _taskIdIndex.TryRemove(kvp.Key, out _);
                    tasksRemoved++;
                }
            }
        }

        if (tasksRemoved > 0)
        {
            _logger.LogDebug("Task store TTL cleanup: removed {TaskCount} tasks", tasksRemoved);
        }
    }
}
