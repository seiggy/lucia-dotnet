using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Wyoming;

public sealed class BackgroundTaskTrackerNotificationTests
{
    [Fact]
    public async Task Version_WhenItChanges_TheNewStateIsAlreadyStored()
    {
        var tracker = new BackgroundTaskTracker(NullLogger<BackgroundTaskTracker>.Instance);

        // The old code bumped the version before it stored the update, so a reader polling the
        // version could see the change and still read the previous state.
        for (var i = 0; i < 2000; i++)
        {
            var handle = tracker.CreateTask("task", ["stage"]);
            var version = tracker.Version;
            var reader = Task.Run(() =>
            {
                SpinWait.SpinUntil(() => tracker.Version != version);
                return tracker.GetTask(handle.TaskId)!.Status;
            });

            handle.MarkRunning();

            Assert.Equal(BackgroundTaskStatus.Running, await reader);
        }
    }
    [Fact]
    public void ReportStageProgress_WhenNothingChanges_DoesNotNotify()
    {
        var tracker = new BackgroundTaskTracker(NullLogger<BackgroundTaskTracker>.Instance);
        var handle = tracker.CreateTask("task", ["stage"]);
        var progress = handle.CreateStageProgress(1);
        progress.Report(0, 50, "half");
        var version = tracker.Version;

        progress.Report(0, 50, "half");

        Assert.Equal(version, tracker.Version);
    }
}
