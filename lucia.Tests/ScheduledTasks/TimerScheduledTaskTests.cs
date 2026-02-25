using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.ScheduledTasks;

public sealed class TimerScheduledTaskTests
{
    [Fact]
    public void IsExpired_ReturnsTrue_WhenFireAtInPast()
    {
        var task = new TimerScheduledTask
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test timer",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Message = "Done!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 60
        };

        Assert.True(task.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenFireAtInFuture()
    {
        var task = new TimerScheduledTask
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test timer",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Message = "Done!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 300
        };

        Assert.False(task.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenFireAtEqualsNow()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TimerScheduledTask
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test timer",
            FireAt = now,
            Message = "Done!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 0
        };

        Assert.True(task.IsExpired(now));
    }

    [Fact]
    public void TaskType_IsTimer()
    {
        var task = new TimerScheduledTask
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test timer",
            FireAt = DateTimeOffset.UtcNow,
            Message = "Done!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 60
        };

        Assert.Equal(ScheduledTaskType.Timer, task.TaskType);
    }

    [Fact]
    public void ToDocument_RoundTripsCorrectly()
    {
        var task = new TimerScheduledTask
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "5 minute timer",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Message = "Timer is up!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 300
        };

        var doc = task.ToDocument();

        Assert.Equal("t1", doc.Id);
        Assert.Equal("task-t1", doc.TaskId);
        Assert.Equal("5 minute timer", doc.Label);
        Assert.Equal(task.FireAt, doc.FireAt);
        Assert.Equal(ScheduledTaskType.Timer, doc.TaskType);
        Assert.Equal(ScheduledTaskStatus.Pending, doc.Status);
        Assert.Equal("Timer is up!", doc.Message);
        Assert.Equal("assist_satellite.bedroom", doc.EntityId);
        Assert.Equal(300, doc.DurationSeconds);

        // Verify factory can reconstitute from document
        var reconstituted = ScheduledTaskFactory.FromDocument(doc);
        Assert.NotNull(reconstituted);
        var timer = Assert.IsType<TimerScheduledTask>(reconstituted);
        Assert.Equal(task.Id, timer.Id);
        Assert.Equal(task.Message, timer.Message);
        Assert.Equal(task.EntityId, timer.EntityId);
    }
}
