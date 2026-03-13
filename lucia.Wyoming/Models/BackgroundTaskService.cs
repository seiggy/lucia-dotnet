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
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(work);

        var taskId = Guid.NewGuid().ToString("N")[..12];
        var info = new BackgroundTaskInfo
        {
            Id = taskId,
            Description = description,
            Status = BackgroundTaskStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _tasks[taskId] = info;
        PublishUpdate(info);

        _ = RunTaskAsync(taskId, work);
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

    private async Task RunTaskAsync(
        string taskId,
        Func<string, IProgress<(int percent, string? message)>, CancellationToken, Task> work)
    {
        UpdateTask(taskId, task => task with { Status = BackgroundTaskStatus.Running });

        var progress = new Progress<(int percent, string? message)>(update =>
        {
            UpdateTask(taskId, task => task with
            {
                ProgressPercent = update.percent,
                ProgressMessage = update.message,
            });
        });

        try
        {
            await work(taskId, progress, CancellationToken.None).ConfigureAwait(false);

            UpdateTask(taskId, task => task with
            {
                Status = BackgroundTaskStatus.Complete,
                ProgressPercent = 100,
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
