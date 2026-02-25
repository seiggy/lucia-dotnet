using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.ScheduledTasks;

public sealed class ScheduledTaskFactoryTests
{
    [Fact]
    public void FromDocument_CreatesTimerTask_FromValidDocument()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test timer",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Timer,
            Message = "Time's up!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 300
        };

        var task = ScheduledTaskFactory.FromDocument(doc);

        Assert.NotNull(task);
        Assert.IsType<TimerScheduledTask>(task);
        Assert.Equal("t1", task.Id);
        Assert.Equal(ScheduledTaskType.Timer, task.TaskType);

        var timer = (TimerScheduledTask)task;
        Assert.Equal("Time's up!", timer.Message);
        Assert.Equal("assist_satellite.bedroom", timer.EntityId);
        Assert.Equal(300, timer.DurationSeconds);
    }

    [Fact]
    public void FromDocument_ReturnsNull_WhenTimerMissingMessage()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test timer",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Timer,
            Message = null,
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 300
        };

        var task = ScheduledTaskFactory.FromDocument(doc);
        Assert.Null(task);
    }

    [Fact]
    public void FromDocument_ReturnsNull_WhenTimerMissingEntityId()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test timer",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Timer,
            Message = "Time's up!",
            EntityId = null,
            DurationSeconds = 300
        };

        var task = ScheduledTaskFactory.FromDocument(doc);
        Assert.Null(task);
    }

    [Fact]
    public void FromDocument_ReturnsNull_ForUnknownTaskType()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Unknown task",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = (ScheduledTaskType)99,
        };

        var task = ScheduledTaskFactory.FromDocument(doc);
        Assert.Null(task);
    }
}
