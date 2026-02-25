namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Common interface for all scheduled task types.
/// Implementations define what happens when the task fires and how to persist/restore state.
/// The ScheduledTaskService polls active tasks and calls ExecuteAsync when FireAt is reached.
/// </summary>
public interface IScheduledTask
{
    /// <summary>Unique identifier for this task instance.</summary>
    string Id { get; }

    /// <summary>A2A task ID for external status tracking.</summary>
    string TaskId { get; }

    /// <summary>Human-readable description (e.g., "Morning alarm", "5 minute timer").</summary>
    string Label { get; }

    /// <summary>When this task should execute.</summary>
    DateTimeOffset FireAt { get; }

    /// <summary>Discriminator for polymorphic dispatch.</summary>
    ScheduledTaskType TaskType { get; }

    /// <summary>
    /// Whether this task has passed its fire time and should execute.
    /// </summary>
    bool IsExpired(DateTimeOffset now);

    /// <summary>
    /// Execute the task action (announce, play media, replay orchestrator request, etc.).
    /// Called by the ScheduledTaskService when the task fires.
    /// </summary>
    Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken);

    /// <summary>
    /// Serialize this task to a MongoDB-persistable document for recovery on restart.
    /// </summary>
    ScheduledTaskDocument ToDocument();
}
