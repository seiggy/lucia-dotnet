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

    [Fact]
    public void FromDocument_CreatesAlarmTask_FromValidDocument()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "a1",
            TaskId = "task-a1",
            Label = "Morning Wake Up",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Alarm,
            AlarmClockId = "clock-1",
            TargetEntity = "media_player.bedroom",
            AlarmSoundUri = "media-source://media_source/local/alarms/gentle.wav",
            PlaybackInterval = TimeSpan.FromSeconds(45),
            AutoDismissAfter = TimeSpan.FromMinutes(5)
        };

        var task = ScheduledTaskFactory.FromDocument(doc);

        Assert.NotNull(task);
        Assert.IsType<AlarmScheduledTask>(task);
        Assert.Equal("a1", task.Id);
        Assert.Equal(ScheduledTaskType.Alarm, task.TaskType);

        var alarm = (AlarmScheduledTask)task;
        Assert.Equal("clock-1", alarm.AlarmClockId);
        Assert.Equal("media_player.bedroom", alarm.TargetEntity);
        Assert.Equal("media-source://media_source/local/alarms/gentle.wav", alarm.AlarmSoundUri);
        Assert.Equal(TimeSpan.FromSeconds(45), alarm.PlaybackInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), alarm.AutoDismissAfter);
    }

    [Fact]
    public void FromDocument_ReturnsNull_WhenAlarmMissingClockId()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "a1",
            TaskId = "task-a1",
            Label = "Missing alarm clock ID",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Alarm,
            AlarmClockId = null,
            TargetEntity = "media_player.bedroom",
        };

        var task = ScheduledTaskFactory.FromDocument(doc);
        Assert.Null(task);
    }

    [Fact]
    public void FromDocument_ReturnsNull_WhenAlarmMissingTargetEntity()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "a1",
            TaskId = "task-a1",
            Label = "Missing target entity",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Alarm,
            AlarmClockId = "clock-1",
            TargetEntity = null,
        };

        var task = ScheduledTaskFactory.FromDocument(doc);
        Assert.Null(task);
    }

    [Fact]
    public void FromDocument_AlarmTask_UsesDefaultIntervals_WhenNullInDoc()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "a1",
            TaskId = "task-a1",
            Label = "Alarm with defaults",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Alarm,
            AlarmClockId = "clock-1",
            TargetEntity = "media_player.bedroom",
            PlaybackInterval = null,
            AutoDismissAfter = null
        };

        var task = ScheduledTaskFactory.FromDocument(doc);

        Assert.NotNull(task);
        var alarm = Assert.IsType<AlarmScheduledTask>(task);
        Assert.Equal(TimeSpan.FromSeconds(30), alarm.PlaybackInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), alarm.AutoDismissAfter);
    }
}
