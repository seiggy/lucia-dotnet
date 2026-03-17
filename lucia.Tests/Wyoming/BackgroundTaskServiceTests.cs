using lucia.Wyoming.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class BackgroundTaskServiceTests
{
    [Fact]
    public async Task QueueAndProcess_CompletesTask()
    {
        var (tracker, queue, processor) = CreateServices();
        await processor.StartAsync(CancellationToken.None);

        var handle = tracker.CreateTask("Test task", ["Working"]);
        var completed = new TaskCompletionSource();

        await queue.QueueBackgroundWorkItemAsync(async ct =>
        {
            handle.MarkRunning();
            var stages = handle.CreateStageProgress(1);
            stages.Report(0, 50, "Halfway");
            await Task.Yield();
            stages.Report(0, 100, "Done");
            handle.MarkComplete(100);
            completed.SetResult();
        });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var task = tracker.GetTask(handle.TaskId);
        Assert.NotNull(task);
        Assert.Equal(BackgroundTaskStatus.Complete, task.Status);
        Assert.Equal(100, task.ProgressPercent);

        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QueueAndProcess_FailedTask_TracksError()
    {
        var (tracker, queue, processor) = CreateServices();
        await processor.StartAsync(CancellationToken.None);

        var handle = tracker.CreateTask("Failing task", ["Working"]);
        var completed = new TaskCompletionSource();

        await queue.QueueBackgroundWorkItemAsync(async ct =>
        {
            handle.MarkRunning();
            handle.MarkFailed("Boom", 50);
            completed.SetResult();
            await Task.CompletedTask;
        });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var task = tracker.GetTask(handle.TaskId);
        Assert.NotNull(task);
        Assert.Equal(BackgroundTaskStatus.Failed, task.Status);
        Assert.Equal("Boom", task.Error);

        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void PurgeCompleted_RemovesOldTasks()
    {
        var (tracker, _, _) = CreateServices();
        var handle = tracker.CreateTask("Old task", ["Working"]);
        handle.MarkRunning();
        handle.MarkComplete(100);

        tracker.PurgeCompleted(TimeSpan.Zero);

        Assert.Null(tracker.GetTask(handle.TaskId));
    }

    private static (BackgroundTaskTracker tracker, IBackgroundTaskQueue queue, BackgroundTaskProcessor processor) CreateServices()
    {
        var tracker = new BackgroundTaskTracker(NullLogger<BackgroundTaskTracker>.Instance);
        var queue = new BackgroundTaskQueue(capacity: 10);
        var processor = new BackgroundTaskProcessor(queue, NullLogger<BackgroundTaskProcessor>.Instance);
        return (tracker, queue, processor);
    }
}
