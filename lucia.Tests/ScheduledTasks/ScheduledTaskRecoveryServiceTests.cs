using FakeItEasy;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.ScheduledTasks;

public sealed class ScheduledTaskRecoveryServiceTests
{
    private readonly ScheduledTaskStore _store = new();
    private readonly IScheduledTaskRepository _repository = A.Fake<IScheduledTaskRepository>();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private readonly ILogger<ScheduledTaskRecoveryService> _logger = A.Fake<ILogger<ScheduledTaskRecoveryService>>();

    [Fact]
    public async Task RecoversRecentTimerTasks()
    {
        var now = _timeProvider.GetUtcNow();
        var doc = new ScheduledTaskDocument
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Recent timer",
            FireAt = now.AddMinutes(2),
            TaskType = ScheduledTaskType.Timer,
            Status = ScheduledTaskStatus.Pending,
            Message = "Timer is up!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 120
        };

        A.CallTo(() => _repository.GetRecoverableTasksAsync(A<CancellationToken>._))
            .Returns(new List<ScheduledTaskDocument> { doc });

        var service = new ScheduledTaskRecoveryService(_store, _repository, _timeProvider, _logger);
        await service.StartAsync(CancellationToken.None);
        // BackgroundService.StartAsync fires ExecuteAsync asynchronously — wait for completion
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, _store.Count);
        Assert.True(_store.TryGet("t1", out var task));
        Assert.IsType<TimerScheduledTask>(task);
    }

    [Fact]
    public async Task MarksOldTasksAsFailed()
    {
        var now = _timeProvider.GetUtcNow();
        var doc = new ScheduledTaskDocument
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Old timer",
            FireAt = now.AddHours(-1),
            TaskType = ScheduledTaskType.Timer,
            Status = ScheduledTaskStatus.Pending,
            Message = "Timer is up!",
            EntityId = "assist_satellite.bedroom",
            DurationSeconds = 60
        };

        A.CallTo(() => _repository.GetRecoverableTasksAsync(A<CancellationToken>._))
            .Returns(new List<ScheduledTaskDocument> { doc });

        var service = new ScheduledTaskRecoveryService(_store, _repository, _timeProvider, _logger);
        await service.StartAsync(CancellationToken.None);
        // BackgroundService.StartAsync fires ExecuteAsync asynchronously — wait for completion
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.True(_store.IsEmpty, "Old task should not be recovered into store");
        A.CallTo(() => _repository.UpdateStatusAsync("t1", ScheduledTaskStatus.Failed, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SkipsUnknownTaskTypes()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Unknown type",
            FireAt = _timeProvider.GetUtcNow().AddMinutes(5),
            TaskType = (ScheduledTaskType)99,
            Status = ScheduledTaskStatus.Pending,
        };

        A.CallTo(() => _repository.GetRecoverableTasksAsync(A<CancellationToken>._))
            .Returns(new List<ScheduledTaskDocument> { doc });

        var service = new ScheduledTaskRecoveryService(_store, _repository, _timeProvider, _logger);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.True(_store.IsEmpty);
    }

    [Fact]
    public async Task HandlesEmptyRepository()
    {
        A.CallTo(() => _repository.GetRecoverableTasksAsync(A<CancellationToken>._))
            .Returns(new List<ScheduledTaskDocument>());

        var service = new ScheduledTaskRecoveryService(_store, _repository, _timeProvider, _logger);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.True(_store.IsEmpty);
    }
}
