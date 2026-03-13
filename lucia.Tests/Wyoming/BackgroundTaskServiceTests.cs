using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class BackgroundTaskServiceTests
{
    [Fact]
    public async Task StartTask_CompletesAndStoresLatestProgress()
    {
        var service = CreateService();

        var taskId = service.StartTask(
            "Download model",
            async (_, progress, ct) =>
            {
                progress.Report((25, "Starting"));
                progress.Report((80, "Almost done"));
                await Task.Yield();
            });

        var task = await WaitForTerminalStateAsync(service, taskId);

        Assert.Equal(BackgroundTaskStatus.Complete, task.Status);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Equal("Almost done", task.ProgressMessage);
        Assert.Null(task.Error);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public async Task StartTask_FailureMarksTaskAsFailed()
    {
        var service = CreateService();

        var taskId = service.StartTask(
            "Download model",
            (_, _, _) => Task.FromException(new InvalidOperationException("Boom")));

        var task = await WaitForTerminalStateAsync(service, taskId);

        Assert.Equal(BackgroundTaskStatus.Failed, task.Status);
        Assert.Equal("Boom", task.Error);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public async Task PurgeCompleted_RemovesTerminalTasksOlderThanMaxAge()
    {
        var service = CreateService();

        var taskId = service.StartTask(
            "Download model",
            (_, _, _) => Task.CompletedTask);

        _ = await WaitForTerminalStateAsync(service, taskId);

        service.PurgeCompleted(TimeSpan.Zero);

        Assert.Null(service.GetTask(taskId));
    }

    private static BackgroundTaskService CreateService() =>
        new(NullLogger<BackgroundTaskService>.Instance);

    private static async Task<BackgroundTaskInfo> WaitForTerminalStateAsync(
        BackgroundTaskService service,
        string taskId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var task = service.GetTask(taskId);
            if (task is { Status: BackgroundTaskStatus.Complete or BackgroundTaskStatus.Failed or BackgroundTaskStatus.Cancelled })
            {
                return task;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Task {taskId} did not reach a terminal state.");
    }
}
