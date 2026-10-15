namespace lucia.Wyoming.Models;

/// <summary>
/// Handle returned by <see cref="BackgroundTaskTracker.CreateTask"/> that the work item
/// uses to report lifecycle and progress. Encapsulates the taskId + tracker reference.
/// </summary>
public sealed class TaskHandle(string taskId, string description, BackgroundTaskTracker tracker)
{
    public string TaskId => taskId;

    public StageProgress CreateStageProgress(int stageCount)
        => new(taskId, stageCount, tracker);

    public void MarkRunning() => tracker.MarkRunning(taskId, description);

    public void MarkComplete(double durationMs) => tracker.MarkComplete(taskId, description, durationMs);

    public void MarkFailed(string error, double durationMs) => tracker.MarkFailed(taskId, description, error, durationMs);
}
