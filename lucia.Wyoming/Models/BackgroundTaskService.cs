using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Models;

public sealed class BackgroundTaskService(ILogger<BackgroundTaskService> logger)
{
    private readonly ConcurrentDictionary<string, BackgroundTaskInfo> _tasks = new();
    private readonly Channel<BackgroundTaskInfo> _updateChannel = Channel.CreateUnbounded<BackgroundTaskInfo>();

    public string StartTask(
        string description,
        Func<string, IProgress<(int percent, string? message)>, CancellationToken, Task> work)
    {
        return StartStagedTask(description, ["Working"], async (id, stages, ct) =>
        {
            var progress = new DirectProgress(update =>
                stages.Report(0, update.percent, update.message));
            await work(id, progress, ct).ConfigureAwait(false);
        });
    }

    /// <summary>Synchronous progress reporter that avoids SynchronizationContext.Post delays.</summary>
    private sealed class DirectProgress(Action<(int percent, string? message)> handler)
        : IProgress<(int percent, string? message)>
    {
        public void Report((int percent, string? message) value) => handler(value);
    }

    /// <summary>
    /// Start a multi-stage background task. Each stage has its own 0-100% progress.
    /// </summary>
    public string StartStagedTask(
        string description,
        string[] stageNames,
        Func<string, StageProgress, CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(work);

        var taskId = Guid.NewGuid().ToString("N")[..12];
        var stages = stageNames.Select(name => new BackgroundTaskStage
        {
            Name = name,
            Status = BackgroundTaskStatus.Queued,
        }).ToList();

        var info = new BackgroundTaskInfo
        {
            Id = taskId,
            Description = description,
            Status = BackgroundTaskStatus.Queued,
            Stages = stages,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _tasks[taskId] = info;
        PublishUpdate(info);

        var stageProgress = new StageProgress(taskId, stageNames.Length, this);
        _ = RunStagedTaskAsync(taskId, stageProgress, work);
        return taskId;
    }

    public IReadOnlyList<BackgroundTaskInfo> GetAllTasks() =>
        _tasks.Values
            .OrderByDescending(task => task.CreatedAt)
            .ToList();

    public BackgroundTaskInfo? GetTask(string taskId) =>
        _tasks.GetValueOrDefault(taskId);

    public ChannelReader<BackgroundTaskInfo> GetUpdateReader() =>
        _updateChannel.Reader;

    /// <summary>
    /// Clean up completed tasks older than the specified age.
    /// </summary>
    public void PurgeCompleted(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var toRemove = _tasks.Values
            .Where(task => task.Status is BackgroundTaskStatus.Complete or BackgroundTaskStatus.Failed)
            .Where(task => task.CompletedAt < cutoff)
            .Select(task => task.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            _tasks.TryRemove(id, out _);
        }
    }

    private async Task RunStagedTaskAsync(
        string taskId,
        StageProgress stageProgress,
        Func<string, StageProgress, CancellationToken, Task> work)
    {
        UpdateTask(taskId, task => task with { Status = BackgroundTaskStatus.Running });

        try
        {
            await work(taskId, stageProgress, CancellationToken.None).ConfigureAwait(false);

            UpdateTask(taskId, task => task with
            {
                Status = BackgroundTaskStatus.Complete,
                ProgressPercent = 100,
                Stages = task.Stages.Select(s => s with
                {
                    Status = BackgroundTaskStatus.Complete,
                    ProgressPercent = 100,
                }).ToList(),
                CompletedAt = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background task {TaskId} failed", taskId);

            UpdateTask(taskId, task => task with
            {
                Status = BackgroundTaskStatus.Failed,
                Error = ex.Message,
                CompletedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    internal void ReportStageProgress(string taskId, int stageIndex, int percent, string? message)
    {
        UpdateTask(taskId, task =>
        {
            var stages = task.Stages.ToList();
            if (stageIndex >= stages.Count) return task;

            stages[stageIndex] = stages[stageIndex] with
            {
                Status = percent >= 100 ? BackgroundTaskStatus.Complete : BackgroundTaskStatus.Running,
                ProgressPercent = Math.Clamp(percent, 0, 100),
                ProgressMessage = message,
            };

            // Overall progress: average of all stages
            var overallPercent = (int)stages.Average(s => s.ProgressPercent);
            var currentMessage = stages.LastOrDefault(s => s.Status == BackgroundTaskStatus.Running)?.ProgressMessage
                              ?? stages.LastOrDefault(s => s.Status == BackgroundTaskStatus.Complete)?.ProgressMessage;

            return task with
            {
                Stages = stages,
                ProgressPercent = overallPercent,
                ProgressMessage = currentMessage,
            };
        });
    }

    private void UpdateTask(string taskId, Func<BackgroundTaskInfo, BackgroundTaskInfo> transform)
    {
        _tasks.AddOrUpdate(
            taskId,
            _ => throw new InvalidOperationException($"Task {taskId} not found"),
            (_, existing) =>
            {
                var updated = transform(existing);
                PublishUpdate(updated);
                return updated;
            });
    }

    private void PublishUpdate(BackgroundTaskInfo info)
    {
        _updateChannel.Writer.TryWrite(info);
    }
}
