using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IScheduledTaskRepository"/> provider must share.
/// </summary>
public abstract class ScheduledTaskRepositoryContract
{
    protected abstract Task<StoreScope<IScheduledTaskRepository>> CreateAsync();

    [Fact]
    public async Task GetRecoverableTasksAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.GetRecoverableTasksAsync());
    }

    [Fact]
    public async Task UpsertAsync_NewTask_RoundTripsAllFields()
    {
        await using var scope = await CreateAsync();

        var document = new ScheduledTaskDocument
        {
            Id = "task-1",
            TaskId = "a2a-42",
            Label = "Wake up",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Alarm,
            Status = ScheduledTaskStatus.Pending,
            Message = "Good morning",
            EntityId = "media_player.bedroom",
            DurationSeconds = 90,
            AlarmClockId = "alarm-7",
            TargetEntity = "media_player.kitchen",
            AlarmSoundUri = "media-source://media_source/local/alarms/gentle.wav",
            PlaybackInterval = TimeSpan.FromSeconds(30),
            AutoDismissAfter = TimeSpan.FromMinutes(10),
            VolumeStart = 0.2,
            VolumeEnd = 0.8,
            VolumeRampDuration = TimeSpan.FromSeconds(15),
            Prompt = "Turn on the lights",
            TargetAgentId = "agent-3",
            EntityContext = "Bedroom",
        };

        await scope.Store.UpsertAsync(document);

        var loaded = await scope.Store.GetByIdAsync(document.Id);
        Assert.NotNull(loaded);
        Assert.Equal(document.TaskId, loaded!.TaskId);
        Assert.Equal(document.Label, loaded.Label);
        Assert.Equal(document.FireAt, loaded.FireAt);
        Assert.Equal(document.TaskType, loaded.TaskType);
        Assert.Equal(document.Status, loaded.Status);
        Assert.Equal(document.Message, loaded.Message);
        Assert.Equal(document.EntityId, loaded.EntityId);
        Assert.Equal(document.DurationSeconds, loaded.DurationSeconds);
        Assert.Equal(document.AlarmClockId, loaded.AlarmClockId);
        Assert.Equal(document.TargetEntity, loaded.TargetEntity);
        Assert.Equal(document.AlarmSoundUri, loaded.AlarmSoundUri);
        Assert.Equal(document.PlaybackInterval, loaded.PlaybackInterval);
        Assert.Equal(document.AutoDismissAfter, loaded.AutoDismissAfter);
        Assert.Equal(document.VolumeStart, loaded.VolumeStart);
        Assert.Equal(document.VolumeEnd, loaded.VolumeEnd);
        Assert.Equal(document.VolumeRampDuration, loaded.VolumeRampDuration);
        Assert.Equal(document.Prompt, loaded.Prompt);
        Assert.Equal(document.TargetAgentId, loaded.TargetAgentId);
        Assert.Equal(document.EntityContext, loaded.EntityContext);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatusWithoutDuplicatingRecords()
    {
        await using var scope = await CreateAsync();
        var task = new ScheduledTaskDocument
        {
            Id = "task-2",
            TaskId = "a2a-99",
            Label = "Remind me",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(5),
            TaskType = ScheduledTaskType.Timer,
        };

        await scope.Store.UpsertAsync(task);
        await scope.Store.UpdateStatusAsync(task.Id, ScheduledTaskStatus.Active);

        var saved = await scope.Store.GetByIdAsync(task.Id);
        Assert.NotNull(saved);
        Assert.Equal(ScheduledTaskStatus.Active, saved!.Status);

        var recoverable = await scope.Store.GetRecoverableTasksAsync();
        Assert.Contains(recoverable, record => record.Id == task.Id && record.Status == ScheduledTaskStatus.Active);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheTargetTask()
    {
        await using var scope = await CreateAsync();
        var first = new ScheduledTaskDocument { Id = "task-a", TaskId = "a2a-a", Label = "first", FireAt = DateTimeOffset.UtcNow, TaskType = ScheduledTaskType.Timer };
        var second = new ScheduledTaskDocument { Id = "task-b", TaskId = "a2a-b", Label = "second", FireAt = DateTimeOffset.UtcNow.AddMinutes(5), TaskType = ScheduledTaskType.Timer };

        await scope.Store.UpsertAsync(first);
        await scope.Store.UpsertAsync(second);

        await scope.Store.DeleteAsync(first.Id);

        Assert.Null(await scope.Store.GetByIdAsync(first.Id));
        Assert.NotNull(await scope.Store.GetByIdAsync(second.Id));
    }

    [Fact]
    public async Task PurgeCompletedAsync_DeletesOnlyExpiredCompletedStatuses()
    {
        await using var scope = await CreateAsync();
        var now = DateTimeOffset.UtcNow;

        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "old-complete", TaskId = "a2a-old-complete", Label = "old complete", FireAt = now.AddMinutes(-10), TaskType = ScheduledTaskType.Timer, Status = ScheduledTaskStatus.Completed });
        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "old-failed", TaskId = "a2a-old-failed", Label = "old failed", FireAt = now.AddMinutes(-20), TaskType = ScheduledTaskType.Timer, Status = ScheduledTaskStatus.Failed });
        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "fresh-complete", TaskId = "a2a-fresh-complete", Label = "fresh complete", FireAt = now.AddMinutes(-1), TaskType = ScheduledTaskType.Timer, Status = ScheduledTaskStatus.Completed });
        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "pending", TaskId = "a2a-pending", Label = "pending", FireAt = now.AddMinutes(10), TaskType = ScheduledTaskType.Timer, Status = ScheduledTaskStatus.Pending });
        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "active", TaskId = "a2a-active", Label = "active", FireAt = now.AddMinutes(1), TaskType = ScheduledTaskType.Alarm, Status = ScheduledTaskStatus.Active });

        var deleted = await scope.Store.PurgeCompletedAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(2, deleted);
        Assert.Null(await scope.Store.GetByIdAsync("old-complete"));
        Assert.Null(await scope.Store.GetByIdAsync("old-failed"));
        Assert.NotNull(await scope.Store.GetByIdAsync("fresh-complete"));
        Assert.NotNull(await scope.Store.GetByIdAsync("pending"));
        Assert.NotNull(await scope.Store.GetByIdAsync("active"));
    }

    [Fact]
    public async Task GetRecoverableTasksAsync_OnlyReturnsPendingAndActiveTasks()
    {
        await using var scope = await CreateAsync();

        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "pending-1", TaskId = "a2a-pending-1", Label = "pending", FireAt = DateTimeOffset.UtcNow.AddMinutes(1), TaskType = ScheduledTaskType.Timer, Status = ScheduledTaskStatus.Pending });
        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "active-1", TaskId = "a2a-active-1", Label = "active", FireAt = DateTimeOffset.UtcNow.AddMinutes(2), TaskType = ScheduledTaskType.Alarm, Status = ScheduledTaskStatus.Active });
        await scope.Store.UpsertAsync(new ScheduledTaskDocument { Id = "completed-1", TaskId = "a2a-completed-1", Label = "completed", FireAt = DateTimeOffset.UtcNow.AddMinutes(3), TaskType = ScheduledTaskType.Timer, Status = ScheduledTaskStatus.Completed });

        var tasks = await scope.Store.GetRecoverableTasksAsync();

        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, task => Assert.True(task.Status == ScheduledTaskStatus.Pending || task.Status == ScheduledTaskStatus.Active));
    }
}
