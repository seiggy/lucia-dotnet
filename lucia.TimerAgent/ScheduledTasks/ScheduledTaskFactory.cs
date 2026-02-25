namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// Creates <see cref="IScheduledTask"/> instances from persisted <see cref="ScheduledTaskDocument"/>
/// records. Used by <see cref="ScheduledTaskRecoveryService"/> to hydrate tasks on startup.
/// </summary>
public static class ScheduledTaskFactory
{
    /// <summary>
    /// Reconstitutes an <see cref="IScheduledTask"/> from a MongoDB document.
    /// Returns null if the task type is unknown or the document is invalid.
    /// </summary>
    public static IScheduledTask? FromDocument(ScheduledTaskDocument doc)
    {
        return doc.TaskType switch
        {
            ScheduledTaskType.Timer => CreateTimerTask(doc),
            // Alarm and AgentTask types will be added in later phases
            _ => null
        };
    }

    private static TimerScheduledTask? CreateTimerTask(ScheduledTaskDocument doc)
    {
        if (doc.Message is null || doc.EntityId is null)
            return null;

        return new TimerScheduledTask
        {
            Id = doc.Id,
            TaskId = doc.TaskId,
            Label = doc.Label,
            FireAt = doc.FireAt,
            Message = doc.Message,
            EntityId = doc.EntityId,
            DurationSeconds = doc.DurationSeconds ?? 0
        };
    }
}
