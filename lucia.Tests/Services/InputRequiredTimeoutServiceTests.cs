using A2A;
using FakeItEasy;
using lucia.Agents.Configuration;
using lucia.Agents.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using InMemoryTaskStore = lucia.Data.InMemory.InMemoryTaskStore;

namespace lucia.Tests.Services;

/// <summary>
/// Unit tests for <see cref="InputRequiredTimeoutService"/>.
/// Uses <see cref="FakeTimeProvider"/> for deterministic time control — no real
/// waits of 30 s occur.  The <c>InMemoryTaskStore</c> serves as both the
/// <c>ITaskStore</c> and <c>ITaskIdIndex</c> backing store.
/// </summary>
public sealed class InputRequiredTimeoutServiceTests : IDisposable
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock;
    private readonly InMemoryTaskStore _store;
    private readonly ILogger<InputRequiredTimeoutService> _logger;

    public InputRequiredTimeoutServiceTests()
    {
        _clock = new FakeTimeProvider(BaseTime);
        var storeLogger = A.Fake<ILogger<InMemoryTaskStore>>();
        _store = new InMemoryTaskStore(storeLogger);
        _logger = A.Fake<ILogger<InputRequiredTimeoutService>>();
    }

    public void Dispose() => _store.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────

    private InputRequiredTimeoutService CreateService(TimeSpan timeout)
    {
        var options = Options.Create(new InputRequiredTimeoutOptions
        {
            Timeout = timeout,
            SweepInterval = TimeSpan.FromSeconds(10)
        });

        return new InputRequiredTimeoutService(_store, _store, _clock, options, _logger);
    }

    private async Task<AgentTask> SaveInputRequiredTaskAsync(
        string taskId,
        DateTimeOffset inputRequiredAt)
    {
        var task = new AgentTask
        {
            Id = taskId,
            ContextId = "ctx-" + taskId,
            Status = new A2A.TaskStatus
            {
                State = TaskState.InputRequired,
                Timestamp = inputRequiredAt
            },
            History = []
        };

        await _store.SaveTaskAsync(taskId, task);
        return task;
    }

    // ── Scenario (a): input arrives (task completes) before timeout ──────

    [Fact]
    public async Task SweepAsync_TaskInInputRequired_WithinTimeout_IsNotCancelled()
    {
        // Arrange: task entered InputRequired 10 s ago; timeout is 30 s
        var taskId = "task-within";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(10));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        // Act
        await svc.SweepAsync(CancellationToken.None);

        // Assert: task is still InputRequired (not cancelled)
        var task = await _store.GetTaskAsync(taskId);
        Assert.NotNull(task);
        Assert.Equal(TaskState.InputRequired, task.Status.State);
    }

    [Fact]
    public async Task SweepAsync_TaskCompletedBeforeTimeout_IsLeftAlone()
    {
        // Arrange: task was InputRequired but the user has already provided input
        // (state is now Working); sweeper must not re-cancel it.
        var taskId = "task-completed";
        var task = new AgentTask
        {
            Id = taskId,
            ContextId = "ctx-" + taskId,
            Status = new A2A.TaskStatus
            {
                State = TaskState.Working,
                Timestamp = BaseTime - TimeSpan.FromSeconds(5)
            },
            History = []
        };
        await _store.SaveTaskAsync(taskId, task);

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        // Act
        await svc.SweepAsync(CancellationToken.None);

        // Assert: task still Working
        var result = await _store.GetTaskAsync(taskId);
        Assert.NotNull(result);
        Assert.Equal(TaskState.Working, result.Status.State);
    }

    // ── Scenario (b): no input within window → auto-cancel ──────────────

    [Fact]
    public async Task SweepAsync_TaskInInputRequired_PastTimeout_AutoCancelled()
    {
        // Arrange: task entered InputRequired 35 s ago; timeout is 30 s
        var taskId = "task-stale";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(35));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        // Act
        await svc.SweepAsync(CancellationToken.None);

        // Assert: task is now Canceled
        var task = await _store.GetTaskAsync(taskId);
        Assert.NotNull(task);
        Assert.Equal(TaskState.Canceled, task.Status.State);
        Assert.NotNull(task.Status.Message);
        Assert.True(task.History?.Count > 0, "History should contain the cancel message");
    }

    [Fact]
    public async Task SweepAsync_ExactlyAtTimeoutBoundary_AutoCancelled()
    {
        // A task sitting at exactly the timeout age should be cancelled
        var taskId = "task-exact";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(30));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        await svc.SweepAsync(CancellationToken.None);

        var task = await _store.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Canceled, task!.Status.State);
    }

    [Fact]
    public async Task SweepAsync_OnlyInputRequiredTasks_AreCancelled_OtherStatesUntouched()
    {
        // Arrange: mix of states — only InputRequired past timeout should be cancelled
        await SaveInputRequiredTaskAsync("stale-input", BaseTime - TimeSpan.FromSeconds(60));

        var working = new AgentTask
        {
            Id = "working",
            ContextId = "ctx",
            Status = new A2A.TaskStatus { State = TaskState.Working, Timestamp = BaseTime - TimeSpan.FromSeconds(60) },
            History = []
        };
        await _store.SaveTaskAsync("working", working);

        var completed = new AgentTask
        {
            Id = "completed",
            ContextId = "ctx",
            Status = new A2A.TaskStatus { State = TaskState.Completed, Timestamp = BaseTime - TimeSpan.FromSeconds(60) },
            History = []
        };
        await _store.SaveTaskAsync("completed", completed);

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        await svc.SweepAsync(CancellationToken.None);

        Assert.Equal(TaskState.Canceled, (await _store.GetTaskAsync("stale-input"))!.Status.State);
        Assert.Equal(TaskState.Working, (await _store.GetTaskAsync("working"))!.Status.State);
        Assert.Equal(TaskState.Completed, (await _store.GetTaskAsync("completed"))!.Status.State);
    }

    // ── Scenario (c): input arrives AFTER timeout → idempotent, no throw ─

    [Fact]
    public async Task SweepAsync_CalledTwice_AlreadyCancelledTask_IsIdempotent()
    {
        // Arrange: task is past timeout
        var taskId = "task-idempotent";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(60));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        // First sweep — should cancel
        await svc.SweepAsync(CancellationToken.None);
        var afterFirst = await _store.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Canceled, afterFirst!.Status.State);

        // Second sweep — task is no longer InputRequired; should be a no-op
        var ex = await Record.ExceptionAsync(() => svc.SweepAsync(CancellationToken.None));
        Assert.Null(ex);

        var afterSecond = await _store.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Canceled, afterSecond!.Status.State);
        // History count should not grow from the second sweep
        Assert.Equal(afterFirst.History?.Count, afterSecond.History?.Count);
    }

    [Fact]
    public async Task SweepAsync_TaskTransitionedByInputAfterTimeout_NotDoubleCancelled()
    {
        // Simulate: sweeper ran and cancelled; now user input arrives and LuciaEngine
        // sets it back to Working.  The next sweep should leave it alone.
        var taskId = "task-late-input";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(60));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        // First sweep cancels
        await svc.SweepAsync(CancellationToken.None);

        // Simulate user input arriving (LuciaEngine sets state back to Working)
        var task = await _store.GetTaskAsync(taskId);
        task!.Status = new A2A.TaskStatus { State = TaskState.Working, Timestamp = _clock.GetUtcNow() };
        await _store.SaveTaskAsync(taskId, task);

        // Second sweep — must not re-cancel the now-Working task
        await svc.SweepAsync(CancellationToken.None);

        var final = await _store.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Working, final!.Status.State);
    }

    // ── Scenario (d): timeout value read from configuration ──────────────

    [Fact]
    public async Task SweepAsync_RespectsConfiguredTimeout_ShortTimeout()
    {
        // Override timeout to 5 s; a task 8 s old should be cancelled
        var taskId = "task-short-timeout";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(8));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(5));

        await svc.SweepAsync(CancellationToken.None);

        var task = await _store.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Canceled, task!.Status.State);
    }

    [Fact]
    public async Task SweepAsync_RespectsConfiguredTimeout_LongTimeout_DoesNotCancel()
    {
        // Override timeout to 120 s; a task 60 s old should NOT be cancelled
        var taskId = "task-long-timeout";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(60));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(120));

        await svc.SweepAsync(CancellationToken.None);

        var task = await _store.GetTaskAsync(taskId);
        Assert.Equal(TaskState.InputRequired, task!.Status.State);
    }

    [Fact]
    public async Task SweepAsync_CancelledTaskTimestampIsSetByTimeProvider()
    {
        // The cancel timestamp should be sourced from TimeProvider, not DateTimeOffset.UtcNow
        var taskId = "task-ts-check";
        await SaveInputRequiredTaskAsync(taskId, BaseTime - TimeSpan.FromSeconds(60));

        // Advance the fake clock to a known future time
        _clock.Advance(TimeSpan.FromMinutes(5));
        var expectedCancelTime = _clock.GetUtcNow();

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        await svc.SweepAsync(CancellationToken.None);

        var task = await _store.GetTaskAsync(taskId);
        Assert.Equal(TaskState.Canceled, task!.Status.State);
        // Cancel timestamp should be at or after the advanced clock time
        Assert.True(task.Status.Timestamp >= expectedCancelTime,
            $"Expected cancel timestamp >= {expectedCancelTime}, got {task.Status.Timestamp}");
    }

    // ── edge cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task SweepAsync_EmptyStore_CompletesWithoutError()
    {
        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));
        var ex = await Record.ExceptionAsync(() => svc.SweepAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SweepAsync_MultipleStaleInputRequiredTasks_AllCancelled()
    {
        await SaveInputRequiredTaskAsync("t1", BaseTime - TimeSpan.FromSeconds(60));
        await SaveInputRequiredTaskAsync("t2", BaseTime - TimeSpan.FromSeconds(90));
        await SaveInputRequiredTaskAsync("t3", BaseTime - TimeSpan.FromSeconds(31));

        using var svc = CreateService(timeout: TimeSpan.FromSeconds(30));

        await svc.SweepAsync(CancellationToken.None);

        Assert.Equal(TaskState.Canceled, (await _store.GetTaskAsync("t1"))!.Status.State);
        Assert.Equal(TaskState.Canceled, (await _store.GetTaskAsync("t2"))!.Status.State);
        Assert.Equal(TaskState.Canceled, (await _store.GetTaskAsync("t3"))!.Status.State);
    }
}
