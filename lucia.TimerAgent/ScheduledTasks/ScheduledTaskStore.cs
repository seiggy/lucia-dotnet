using System.Collections.Concurrent;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Thread-safe in-memory store for active scheduled tasks.
/// Shared between skill layers (writes) and <see cref="ScheduledTaskService"/> (reads/fires).
/// </summary>
public sealed class ScheduledTaskStore
{
    private readonly ConcurrentDictionary<string, IScheduledTask> _tasks = new();

    /// <summary>
    /// Adds or replaces a task in the store.
    /// </summary>
    public void Add(IScheduledTask task) => _tasks[task.Id] = task;

    /// <summary>
    /// Attempts to add a task only if no task with the same ID exists.
    /// </summary>
    public bool TryAdd(IScheduledTask task) => _tasks.TryAdd(task.Id, task);

    /// <summary>
    /// Attempts to remove and return a task by its ID.
    /// </summary>
    public bool TryRemove(string taskId, out IScheduledTask? task)
        => _tasks.TryRemove(taskId, out task);

    /// <summary>
    /// Attempts to get a task by its ID.
    /// </summary>
    public bool TryGet(string taskId, out IScheduledTask? task)
        => _tasks.TryGetValue(taskId, out task);

    /// <summary>
    /// Returns a snapshot of all active tasks.
    /// </summary>
    public IReadOnlyCollection<IScheduledTask> GetAll() => _tasks.Values.ToList();

    /// <summary>
    /// Returns all tasks of the specified type.
    /// </summary>
    public IReadOnlyCollection<IScheduledTask> GetByType(ScheduledTaskType taskType)
        => _tasks.Values.Where(t => t.TaskType == taskType).ToList();

    /// <summary>
    /// Returns the number of active tasks.
    /// </summary>
    public int Count => _tasks.Count;

    /// <summary>
    /// Returns true if there are no active tasks.
    /// </summary>
    public bool IsEmpty => _tasks.IsEmpty;
}
