using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.ScheduledTasks;

public sealed class ScheduledTaskStoreTests
{
    private readonly ScheduledTaskStore _store = new();

    private static TimerScheduledTask CreateTask(string id = "test-1", DateTimeOffset? fireAt = null) =>
        new()
        {
            Id = id,
            TaskId = $"task-{id}",
            Label = $"Test timer {id}",
            FireAt = fireAt ?? DateTimeOffset.UtcNow.AddMinutes(5),
            Message = "Time's up!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 300
        };

    [Fact]
    public void Add_StoresTask()
    {
        var task = CreateTask();
        _store.Add(task);

        Assert.Equal(1, _store.Count);
        Assert.False(_store.IsEmpty);
    }

    [Fact]
    public void TryAdd_ReturnsFalse_WhenDuplicate()
    {
        var task = CreateTask();
        Assert.True(_store.TryAdd(task));
        Assert.False(_store.TryAdd(task));
    }

    [Fact]
    public void TryRemove_ReturnsTask_WhenExists()
    {
        var task = CreateTask();
        _store.Add(task);

        Assert.True(_store.TryRemove("test-1", out var removed));
        Assert.Same(task, removed);
        Assert.True(_store.IsEmpty);
    }

    [Fact]
    public void TryRemove_ReturnsFalse_WhenNotFound()
    {
        Assert.False(_store.TryRemove("nonexistent", out _));
    }

    [Fact]
    public void TryGet_ReturnsTask_WhenExists()
    {
        var task = CreateTask();
        _store.Add(task);

        Assert.True(_store.TryGet("test-1", out var found));
        Assert.Same(task, found);
    }

    [Fact]
    public void GetAll_ReturnsSnapshotOfAllTasks()
    {
        _store.Add(CreateTask("a"));
        _store.Add(CreateTask("b"));
        _store.Add(CreateTask("c"));

        var all = _store.GetAll();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetByType_FiltersCorrectly()
    {
        _store.Add(CreateTask("timer-1"));
        _store.Add(CreateTask("timer-2"));

        var timers = _store.GetByType(ScheduledTaskType.Timer);
        Assert.Equal(2, timers.Count);

        var alarms = _store.GetByType(ScheduledTaskType.Alarm);
        Assert.Empty(alarms);
    }

    [Fact]
    public void Add_ReplacesExisting_WithSameId()
    {
        var original = CreateTask("same-id", DateTimeOffset.UtcNow.AddMinutes(5));
        var replacement = CreateTask("same-id", DateTimeOffset.UtcNow.AddMinutes(10));

        _store.Add(original);
        _store.Add(replacement);

        Assert.Equal(1, _store.Count);
        Assert.True(_store.TryGet("same-id", out var found));
        Assert.Equal(replacement.FireAt, found!.FireAt);
    }
}
