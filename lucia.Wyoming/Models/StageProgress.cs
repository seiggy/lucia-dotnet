namespace lucia.Wyoming.Models;

/// <summary>
/// Reports progress for individual stages of a multi-stage background task.
/// </summary>
public sealed class StageProgress(string taskId, int stageCount, BackgroundTaskTracker tracker)
{
    /// <summary>
    /// Report progress for a specific stage (0-based index).
    /// </summary>
    public void Report(int stageIndex, int percent, string? message = null)
    {
        if (stageIndex < 0 || stageIndex >= stageCount) return;
        tracker.ReportStageProgress(taskId, stageIndex, percent, message);
    }
}
