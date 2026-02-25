using FakeItEasy;
using lucia.TimerAgent;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace lucia.Tests.Timer;

/// <summary>
/// Unit tests for <see cref="SchedulerSkill"/>.
/// </summary>
public sealed class SchedulerSkillTests
{
    private readonly ScheduledTaskStore _taskStore = new();
    private readonly IScheduledTaskRepository _taskRepository = A.Fake<IScheduledTaskRepository>();
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILogger<SchedulerSkill> _logger = A.Fake<ILogger<SchedulerSkill>>();
    private readonly SchedulerSkill _skill;

    public SchedulerSkillTests()
    {
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 7, 15, 12, 0, 0, TimeSpan.Zero));
        _skill = new SchedulerSkill(_taskStore, _taskRepository, _timeProvider, _logger);
    }

    // --- ScheduleActionAsync ---

    [Fact]
    public async Task ScheduleActionAsync_ValidInput_CreatesTaskAndReturnsConfirmation()
    {
        var result = await _skill.ScheduleActionAsync(
            "turn off the living room lights",
            1800,
            "Turn off lights");

        Assert.Contains("will execute in", result);
        Assert.Contains("30 minute(s)", result);
        Assert.Contains("turn off the living room lights", result);

        var tasks = _taskStore.GetByType(ScheduledTaskType.AgentTask);
        Assert.Single(tasks);
    }

    [Fact]
    public async Task ScheduleActionAsync_WithEntityContext_StoresContext()
    {
        await _skill.ScheduleActionAsync(
            "turn off the lights",
            600,
            "Lights off",
            entityContext: "living room lights are on at 80%");

        var tasks = _taskStore.GetByType(ScheduledTaskType.AgentTask);
        var task = Assert.Single(tasks);
        var agentTask = Assert.IsType<AgentScheduledTask>(task);
        Assert.Equal("living room lights are on at 80%", agentTask.EntityContext);
    }

    [Fact]
    public async Task ScheduleActionAsync_EmptyPrompt_ReturnsError()
    {
        var result = await _skill.ScheduleActionAsync("", 600, "Test");

        Assert.Equal("Action prompt cannot be empty.", result);
        Assert.Empty(_taskStore.GetByType(ScheduledTaskType.AgentTask));
    }

    [Fact]
    public async Task ScheduleActionAsync_ZeroDelay_ReturnsError()
    {
        var result = await _skill.ScheduleActionAsync("do something", 0, "Test");

        Assert.Equal("Delay must be greater than zero seconds.", result);
        Assert.Empty(_taskStore.GetByType(ScheduledTaskType.AgentTask));
    }

    [Fact]
    public async Task ScheduleActionAsync_PersistsToRepository()
    {
        await _skill.ScheduleActionAsync("lock the door", 300, "Lock door");

        A.CallTo(() => _taskRepository.UpsertAsync(
            A<ScheduledTaskDocument>.That.Matches(d =>
                d.TaskType == ScheduledTaskType.AgentTask &&
                d.Prompt == "lock the door"),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    // --- ScheduleActionAtAsync ---

    [Fact]
    public async Task ScheduleActionAtAsync_ValidFutureTime_CreatesTask()
    {
        // Current time is 12:00 UTC, schedule for 23:00
        var result = await _skill.ScheduleActionAtAsync(
            "lock the front door",
            "23:00",
            "Lock doors");

        Assert.Contains("will execute in", result);
        Assert.Contains("11 hour(s)", result);

        var tasks = _taskStore.GetByType(ScheduledTaskType.AgentTask);
        Assert.Single(tasks);
    }

    [Fact]
    public async Task ScheduleActionAtAsync_PastTimeToday_SchedulesForTomorrow()
    {
        // Current time is 12:00 UTC, schedule for 08:00 → should be tomorrow
        await _skill.ScheduleActionAtAsync(
            "play morning playlist",
            "08:00",
            "Morning music");

        var tasks = _taskStore.GetByType(ScheduledTaskType.AgentTask);
        var task = Assert.Single(tasks);

        // Should be ~20 hours in the future (tomorrow 08:00)
        var now = _timeProvider.GetUtcNow();
        var remaining = task.FireAt - now;
        Assert.True(remaining.TotalHours > 19 && remaining.TotalHours < 21,
            $"Expected ~20 hours, got {remaining.TotalHours:F1} hours");
    }

    [Fact]
    public async Task ScheduleActionAtAsync_InvalidTimeFormat_ReturnsError()
    {
        var result = await _skill.ScheduleActionAtAsync(
            "do something",
            "not-a-time",
            "Test");

        Assert.Contains("Invalid time format", result);
        Assert.Empty(_taskStore.GetByType(ScheduledTaskType.AgentTask));
    }

    [Fact]
    public async Task ScheduleActionAtAsync_EmptyPrompt_ReturnsError()
    {
        var result = await _skill.ScheduleActionAtAsync("", "23:00", "Test");

        Assert.Equal("Action prompt cannot be empty.", result);
    }

    // --- CancelScheduledActionAsync ---

    [Fact]
    public async Task CancelScheduledActionAsync_ExistingTask_RemovesAndReturnsSuccess()
    {
        await _skill.ScheduleActionAsync("turn off lights", 600, "Lights off");
        var tasks = _taskStore.GetByType(ScheduledTaskType.AgentTask);
        var taskId = tasks.First().Id;

        var result = await _skill.CancelScheduledActionAsync(taskId);

        Assert.Contains("has been cancelled", result);
        Assert.Empty(_taskStore.GetByType(ScheduledTaskType.AgentTask));
    }

    [Fact]
    public async Task CancelScheduledActionAsync_NonExistentTask_ReturnsNotFound()
    {
        var result = await _skill.CancelScheduledActionAsync("nonexistent");

        Assert.Contains("No pending scheduled action found", result);
    }

    [Fact]
    public async Task CancelScheduledActionAsync_UpdatesRepositoryStatus()
    {
        await _skill.ScheduleActionAsync("test action", 600, "Test");
        var taskId = _taskStore.GetByType(ScheduledTaskType.AgentTask).First().Id;

        await _skill.CancelScheduledActionAsync(taskId);

        A.CallTo(() => _taskRepository.UpdateStatusAsync(
            taskId,
            ScheduledTaskStatus.Cancelled,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    // --- ListScheduledActions ---

    [Fact]
    public async Task ListScheduledActions_Empty_ReturnsNoActions()
    {
        var result = await _skill.ListScheduledActions();
        Assert.Equal("No pending scheduled actions.", result);
    }

    [Fact]
    public async Task ListScheduledActions_WithTasks_ListsAll()
    {
        await _skill.ScheduleActionAsync("turn off lights", 600, "Lights off");
        await _skill.ScheduleActionAsync("lock the door", 1800, "Lock door");

        var result = await _skill.ListScheduledActions();

        Assert.Contains("Pending scheduled actions:", result);
        Assert.Contains("turn off lights", result);
        Assert.Contains("lock the door", result);
    }

    // --- GetTools ---

    [Fact]
    public void GetTools_ReturnsFourTools()
    {
        var tools = _skill.GetTools();

        Assert.Equal(4, tools.Count);
        var names = tools.Select(t => t.Name).ToList();
        Assert.Contains("ScheduleAction", names);
        Assert.Contains("ScheduleActionAt", names);
        Assert.Contains("CancelScheduledAction", names);
        Assert.Contains("ListScheduledActions", names);
    }
}
