namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Lifecycle status of a scheduled task.
/// </summary>
public enum ScheduledTaskStatus
{
    /// <summary>Task is waiting to fire.</summary>
    Pending,

    /// <summary>Task is currently executing (e.g., alarm is ringing).</summary>
    Active,

    /// <summary>Task completed successfully (timer announced, agent task executed).</summary>
    Completed,

    /// <summary>Task was dismissed by the user (alarm stopped).</summary>
    Dismissed,

    /// <summary>Task was snoozed (alarm rescheduled).</summary>
    Snoozed,

    /// <summary>Task was auto-dismissed after the safety timeout.</summary>
    AutoDismissed,

    /// <summary>Task was cancelled before firing.</summary>
    Cancelled,

    /// <summary>Task execution failed.</summary>
    Failed
}
