using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Models;

public sealed class BackgroundTaskService(ILogger<BackgroundTaskService> logger)
{
    private static readonly ActivitySource ActivitySource = new("lucia.Wyoming.BackgroundTasks");
    private static readonly Meter Meter = new("lucia.Wyoming.BackgroundTasks");

    private static readonly Counter<long> TasksStarted = Meter.CreateCounter<long>(
        "wyoming.tasks.started", "tasks", "Number of background tasks started");
    private static readonly Counter<long> TasksCompleted = Meter.CreateCounter<long>(
        "wyoming.tasks.completed", "tasks", "Number of background tasks completed successfully");
    private static readonly Counter<long> TasksFailed = Meter.CreateCounter<long>(
        "wyoming.tasks.failed", "tasks", "Number of background tasks that failed");
    private static readonly Histogram<double> TaskDuration = Meter.CreateHistogram<double>(
        "wyoming.tasks.duration_ms", "ms", "Duration of background tasks in milliseconds");
    private static readonly UpDownCounter<int> TasksActive = Meter.CreateUpDownCounter<int>(
        "wyoming.tasks.active", "tasks", "Number of currently running background tasks");

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
        _ = Task.Run(() => RunStagedTaskAsync(taskId, description, stageProgress, work));
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
        string description,
        StageProgress stageProgress,
        Func<string, StageProgress, CancellationToken, Task> work)
    {
        using var activity = ActivitySource.StartActivity("background-task", ActivityKind.Internal);
        activity?.SetTag("task.id", taskId);
        activity?.SetTag("task.description", description);

        TasksStarted.Add(1, new KeyValuePair<string, object?>("task.description", description));
        TasksActive.Add(1);
        var sw = Stopwatch.StartNew();

        logger.LogInformation("Background task {TaskId} starting on thread pool: {Description}", taskId, description);
        UpdateTask(taskId, task => task with { Status = BackgroundTaskStatus.Running });

        try
        {
            await work(taskId, stageProgress, CancellationToken.None).ConfigureAwait(false);

            sw.Stop();
            activity?.SetTag("task.status", "complete");
            TasksCompleted.Add(1, new KeyValuePair<string, object?>("task.description", description));
            TaskDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("task.description", description));

            logger.LogInformation("Background task {TaskId} completed in {Duration}ms", taskId, sw.ElapsedMilliseconds);
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
            sw.Stop();
            activity?.SetTag("task.status", "failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            TasksFailed.Add(1, new KeyValuePair<string, object?>("task.description", description));
            TaskDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("task.description", description));

            logger.LogError(ex, "Background task {TaskId} failed after {Duration}ms", taskId, sw.ElapsedMilliseconds);
            UpdateTask(taskId, task => task with
            {
                Status = BackgroundTaskStatus.Failed,
                Error = ex.Message,
                CompletedAt = DateTimeOffset.UtcNow,
            });
        }
        finally
        {
            TasksActive.Add(-1);
        }
    }

    internal void ReportStageProgress(string taskId, int stageIndex, int percent, string? message)
    {
        _tasks.AddOrUpdate(
            taskId,
            _ => throw new InvalidOperationException($"Task {taskId} not found"),
            (_, existing) =>
            {
                var stages = existing.Stages.ToList();
                if (stageIndex >= stages.Count) return existing;

                var currentStage = stages[stageIndex];
                var clampedPercent = Math.Clamp(percent, 0, 100);

                // Skip update if nothing visible changed (same percent + same message)
                if (currentStage.ProgressPercent == clampedPercent
                    && currentStage.ProgressMessage == message)
                {
                    return existing;
                }

                var newStatus = clampedPercent >= 100
                    ? BackgroundTaskStatus.Complete
                    : BackgroundTaskStatus.Running;

                stages[stageIndex] = currentStage with
                {
                    Status = newStatus,
                    ProgressPercent = clampedPercent,
                    ProgressMessage = message,
                };

                var overallPercent = (int)stages.Average(s => s.ProgressPercent);
                var currentMessage = stages.LastOrDefault(s => s.Status == BackgroundTaskStatus.Running)?.ProgressMessage
                                  ?? stages.LastOrDefault(s => s.Status == BackgroundTaskStatus.Complete)?.ProgressMessage;

                var updated = existing with
                {
                    Stages = stages,
                    ProgressPercent = overallPercent,
                    ProgressMessage = currentMessage,
                };

                PublishUpdate(updated);
                return updated;
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
