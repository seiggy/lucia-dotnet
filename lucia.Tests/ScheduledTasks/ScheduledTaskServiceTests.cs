using FakeItEasy;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.ScheduledTasks;

public sealed class ScheduledTaskServiceTests : IDisposable
{
    private readonly ScheduledTaskStore _store = new();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private readonly ILogger<ScheduledTaskService> _logger = A.Fake<ILogger<ScheduledTaskService>>();
    private readonly ServiceCollection _services = new();
    private readonly ServiceProvider _serviceProvider;
    private readonly ScheduledTaskService _service;

    public ScheduledTaskServiceTests()
    {
        // Register a fake IScheduledTaskRepository so the service can update statuses
        _services.AddSingleton(A.Fake<IScheduledTaskRepository>());
        _serviceProvider = _services.BuildServiceProvider();

        _service = new ScheduledTaskService(
            _store,
            _serviceProvider,
            _timeProvider,
            _logger);
    }

    [Fact]
    public async Task ExpiredTask_IsRemovedFromStore_AndExecuted()
    {
        var executed = false;
        var task = new TestScheduledTask
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Test",
            FireAt = _timeProvider.GetUtcNow().AddSeconds(-1),
            OnExecute = () => executed = true
        };

        _store.Add(task);

        // Start the service and let it process one cycle
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var executeTask = _service.StartAsync(cts.Token);

        // Advance time to trigger the first poll
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // Wait briefly for the fire-and-forget to complete
        await Task.Delay(200);

        await _service.StopAsync(CancellationToken.None);

        Assert.True(executed, "Task should have been executed");
        Assert.True(_store.IsEmpty, "Task should have been removed from store");
    }

    [Fact]
    public async Task FutureTask_IsNotFired()
    {
        var executed = false;
        var task = new TestScheduledTask
        {
            Id = "t1",
            TaskId = "task-t1",
            Label = "Future task",
            FireAt = _timeProvider.GetUtcNow().AddMinutes(10),
            OnExecute = () => executed = true
        };

        _store.Add(task);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _service.StartAsync(cts.Token);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(200);
        await _service.StopAsync(CancellationToken.None);

        Assert.False(executed, "Future task should not have been executed");
        Assert.Equal(1, _store.Count);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    /// <summary>
    /// A simple test-only IScheduledTask implementation for verifying the service loop.
    /// </summary>
    private sealed class TestScheduledTask : IScheduledTask
    {
        public required string Id { get; init; }
        public required string TaskId { get; init; }
        public required string Label { get; init; }
        public required DateTimeOffset FireAt { get; init; }
        public ScheduledTaskType TaskType => ScheduledTaskType.Timer;
        public Action? OnExecute { get; init; }

        public bool IsExpired(DateTimeOffset now) => FireAt <= now;

        public Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            OnExecute?.Invoke();
            return Task.CompletedTask;
        }

        public ScheduledTaskDocument ToDocument() => new()
        {
            Id = Id,
            TaskId = TaskId,
            Label = Label,
            FireAt = FireAt,
            TaskType = ScheduledTaskType.Timer,
            Status = ScheduledTaskStatus.Pending
        };
    }
}
