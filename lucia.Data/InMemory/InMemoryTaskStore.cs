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
    private readonly ConcurrentDictionary<string, TimestampedEntry<TaskPushNotificationConfig>> _notifications = new();
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

    public Task SetTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        _tasks[task.Id] = new TimestampedEntry<AgentTask>(task, DefaultTaskTtl);
        _taskIdIndex[task.Id] = 0;

        return Task.CompletedTask;
    }

    public async Task<AgentTaskStatus> UpdateStatusAsync(
        string taskId,
        TaskState status,
        AgentMessage? message = null,
        CancellationToken cancellationToken = default)
    {
        var task = await GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            throw new A2AException(
                $"Task with ID '{taskId}' not found",
                A2AErrorCode.TaskNotFound);
        }

        if (message is not null)
        {
            task.History ??= new List<AgentMessage>();
            task.History.Add(message);
        }

        var newStatus = new AgentTaskStatus
        {
            State = status,
            Message = message,
            Timestamp = DateTimeOffset.UtcNow
        };

        task.Status = newStatus;
        await SetTaskAsync(task, cancellationToken).ConfigureAwait(false);

        return newStatus;
    }

    public Task<TaskPushNotificationConfig?> GetPushNotificationAsync(
        string taskId,
        string notificationConfigId,
        CancellationToken cancellationToken = default)
    {
        var key = $"{taskId}:{notificationConfigId}";
        if (_notifications.TryGetValue(key, out var entry) && !entry.IsExpired)
            return Task.FromResult<TaskPushNotificationConfig?>(entry.Value);

        if (entry is not null)
            _notifications.TryRemove(key, out _);

        return Task.FromResult<TaskPushNotificationConfig?>(null);
    }

    public Task SetPushNotificationConfigAsync(
        TaskPushNotificationConfig pushNotificationConfig,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pushNotificationConfig);

        var key = $"{pushNotificationConfig.TaskId}:default";
        _notifications[key] = new TimestampedEntry<TaskPushNotificationConfig>(pushNotificationConfig, DefaultTaskTtl);

        return Task.CompletedTask;
    }

    public Task<IEnumerable<TaskPushNotificationConfig>> GetPushNotificationsAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"{taskId}:";
        var configs = _notifications
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal) && !kvp.Value.IsExpired)
            .Select(kvp => kvp.Value.Value)
            .ToList();

        return Task.FromResult<IEnumerable<TaskPushNotificationConfig>>(configs);
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

        var notificationsRemoved = 0;
        foreach (var kvp in _notifications)
        {
            if (kvp.Value.IsExpired)
            {
                if (_notifications.TryRemove(kvp.Key, out _))
                    notificationsRemoved++;
            }
        }

        if (tasksRemoved > 0 || notificationsRemoved > 0)
        {
            _logger.LogDebug("Task store TTL cleanup: removed {TaskCount} tasks + {NotifCount} notifications",
                tasksRemoved, notificationsRemoved);
        }
    }
}
