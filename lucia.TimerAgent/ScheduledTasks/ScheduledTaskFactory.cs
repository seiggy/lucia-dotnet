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
            ScheduledTaskType.Alarm => CreateAlarmTask(doc),
            ScheduledTaskType.AgentTask => CreateAgentTask(doc),
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

    private static AlarmScheduledTask? CreateAlarmTask(ScheduledTaskDocument doc)
    {
        if (doc.AlarmClockId is null || doc.TargetEntity is null)
            return null;

        return new AlarmScheduledTask
        {
            Id = doc.Id,
            TaskId = doc.TaskId,
            Label = doc.Label,
            FireAt = doc.FireAt,
            AlarmClockId = doc.AlarmClockId,
            TargetEntity = doc.TargetEntity,
            AlarmSoundUri = doc.AlarmSoundUri,
            PlaybackInterval = doc.PlaybackInterval ?? TimeSpan.FromSeconds(30),
            AutoDismissAfter = doc.AutoDismissAfter ?? TimeSpan.FromMinutes(10),
            VolumeStart = doc.VolumeStart,
            VolumeEnd = doc.VolumeEnd,
            VolumeRampDuration = doc.VolumeRampDuration ?? TimeSpan.FromSeconds(30)
        };
    }

    private static AgentScheduledTask? CreateAgentTask(ScheduledTaskDocument doc)
    {
        if (doc.Prompt is null)
            return null;

        return new AgentScheduledTask
        {
            Id = doc.Id,
            TaskId = doc.TaskId,
            Label = doc.Label,
            FireAt = doc.FireAt,
            Prompt = doc.Prompt,
            TargetAgentId = doc.TargetAgentId,
            EntityContext = doc.EntityContext
        };
    }
}
