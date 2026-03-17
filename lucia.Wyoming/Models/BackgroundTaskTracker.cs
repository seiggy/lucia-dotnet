using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.Models;

/// <summary>
/// Tracks state and progress for background tasks. Provides change notifications
/// for SSE consumers. This is a singleton state store — not responsible for execution.
/// </summary>
public sealed class BackgroundTaskTracker(ILogger<BackgroundTaskTracker> logger)
{
    private static readonly ActivitySource ActivitySource = new("lucia.Wyoming.BackgroundTasks");
    private static readonly Meter Meter = new("lucia.Wyoming.BackgroundTasks");
    private static readonly Counter<long> TasksStarted = Meter.CreateCounter<long>("wyoming.tasks.started", "tasks");
    private static readonly Counter<long> TasksCompleted = Meter.CreateCounter<long>("wyoming.tasks.completed", "tasks");
    private static readonly Counter<long> TasksFailed = Meter.CreateCounter<long>("wyoming.tasks.failed", "tasks");
    private static readonly Histogram<double> TaskDuration = Meter.CreateHistogram<double>("wyoming.tasks.duration_ms", "ms");
    private static readonly UpDownCounter<int> TasksActive = Meter.CreateUpDownCounter<int>("wyoming.tasks.active", "tasks");

    private readonly ConcurrentDictionary<string, BackgroundTaskInfo> _tasks = new();
    private volatile int _version;
    private TaskCompletionSource _changeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Version => _version;

    /// <summary>
    /// Register a new task and return a handle for the work item to report progress through.
    /// Called from the API endpoint before enqueueing work.
    /// </summary>
    public TaskHandle CreateTask(string description, string[] stageNames)
    {
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
        NotifyChange();

        logger.LogInformation("[background-task] Created task {TaskId}: {Description}", taskId, description);
        return new TaskHandle(taskId, description, this);
    }

    internal void MarkRunning(string taskId, string description)
    {
        UpdateTask(taskId, t => t with { Status = BackgroundTaskStatus.Running });
        TasksStarted.Add(1, new KeyValuePair<string, object?>("task.description", description));
        TasksActive.Add(1);
    }

    internal void MarkComplete(string taskId, string description, double durationMs)
    {
        UpdateTask(taskId, t => t with
        {
            Status = BackgroundTaskStatus.Complete,
            ProgressPercent = 100,
            Stages = t.Stages.Select(s => s with { Status = BackgroundTaskStatus.Complete, ProgressPercent = 100 }).ToList(),
            CompletedAt = DateTimeOffset.UtcNow,
        });
        TasksCompleted.Add(1, new KeyValuePair<string, object?>("task.description", description));
        TaskDuration.Record(durationMs, new KeyValuePair<string, object?>("task.description", description));
        TasksActive.Add(-1);
        logger.LogInformation("[background-task] Task {TaskId} completed in {Duration}ms", taskId, durationMs);
    }

    internal void MarkFailed(string taskId, string description, string error, double durationMs)
    {
        UpdateTask(taskId, t => t with
        {
            Status = BackgroundTaskStatus.Failed,
            Error = error,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        TasksFailed.Add(1, new KeyValuePair<string, object?>("task.description", description));
        TaskDuration.Record(durationMs, new KeyValuePair<string, object?>("task.description", description));
        TasksActive.Add(-1);
        logger.LogError("[background-task] Task {TaskId} failed after {Duration}ms: {Error}", taskId, durationMs, error);
    }

    internal void ReportStageProgress(string taskId, int stageIndex, int percent, string? message)
    {
        _tasks.AddOrUpdate(taskId,
            _ => throw new InvalidOperationException($"Task {taskId} not found"),
            (_, existing) =>
            {
                var stages = existing.Stages.ToList();
                if (stageIndex >= stages.Count) return existing;

                var cur = stages[stageIndex];
                var clamped = Math.Clamp(percent, 0, 100);
                if (cur.ProgressPercent == clamped && cur.ProgressMessage == message) return existing;

                stages[stageIndex] = cur with
                {
                    Status = clamped >= 100 ? BackgroundTaskStatus.Complete : BackgroundTaskStatus.Running,
                    ProgressPercent = clamped,
                    ProgressMessage = message,
                };

                var overall = (int)stages.Average(s => s.ProgressPercent);
                var msg = stages.LastOrDefault(s => s.Status == BackgroundTaskStatus.Running)?.ProgressMessage
                       ?? stages.LastOrDefault(s => s.Status == BackgroundTaskStatus.Complete)?.ProgressMessage;

                var updated = existing with { Stages = stages, ProgressPercent = overall, ProgressMessage = msg };
                NotifyChange();
                return updated;
            });
    }

    public IReadOnlyList<BackgroundTaskInfo> GetAllTasks()
        => _tasks.Values.OrderByDescending(t => t.CreatedAt).ToList();

    public BackgroundTaskInfo? GetTask(string taskId)
        => _tasks.GetValueOrDefault(taskId);

    public async Task WaitForChangeAsync(int sinceVersion, CancellationToken ct)
    {
        if (_version != sinceVersion) return;
        var tcs = _changeTcs;
        using var reg = ct.Register(() => tcs.TrySetCanceled());
        await tcs.Task.ConfigureAwait(false);
    }

    public void PurgeCompleted(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var id in _tasks.Values
            .Where(t => t.Status is BackgroundTaskStatus.Complete or BackgroundTaskStatus.Failed)
            .Where(t => t.CompletedAt < cutoff)
            .Select(t => t.Id).ToList())
            _tasks.TryRemove(id, out _);
    }

    private void UpdateTask(string taskId, Func<BackgroundTaskInfo, BackgroundTaskInfo> transform)
    {
        _tasks.AddOrUpdate(taskId,
            _ => throw new InvalidOperationException($"Task {taskId} not found"),
            (_, existing) => { var u = transform(existing); NotifyChange(); return u; });
    }

    private void NotifyChange()
    {
        Interlocked.Increment(ref _version);
        var old = Interlocked.Exchange(ref _changeTcs, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult();
    }
}

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
