using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.ScheduledTasks;

public sealed class AgentScheduledTaskTests
{
    [Fact]
    public void IsExpired_ReturnsTrue_WhenFireAtInPast()
    {
        var task = CreateTask(fireAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(task.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_ReturnsFalse_WhenFireAtInFuture()
    {
        var task = CreateTask(fireAt: DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.False(task.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsExpired_ReturnsTrue_WhenFireAtEqualsNow()
    {
        var now = DateTimeOffset.UtcNow;
        var task = CreateTask(fireAt: now);
        Assert.True(task.IsExpired(now));
    }

    [Fact]
    public void TaskType_IsAgentTask()
    {
        var task = CreateTask();
        Assert.Equal(ScheduledTaskType.AgentTask, task.TaskType);
    }

    [Fact]
    public void ToDocument_RoundTripsCorrectly()
    {
        var task = new AgentScheduledTask
        {
            Id = "at1",
            TaskId = "task-at1",
            Label = "Turn off lights later",
            FireAt = DateTimeOffset.UtcNow.AddMinutes(30),
            Prompt = "turn off the living room lights",
            TargetAgentId = "home-agent",
            EntityContext = "living room lights are on at 80%"
        };

        var doc = task.ToDocument();

        Assert.Equal("at1", doc.Id);
        Assert.Equal("task-at1", doc.TaskId);
        Assert.Equal("Turn off lights later", doc.Label);
        Assert.Equal(task.FireAt, doc.FireAt);
        Assert.Equal(ScheduledTaskType.AgentTask, doc.TaskType);
        Assert.Equal(ScheduledTaskStatus.Pending, doc.Status);
        Assert.Equal("turn off the living room lights", doc.Prompt);
        Assert.Equal("home-agent", doc.TargetAgentId);
        Assert.Equal("living room lights are on at 80%", doc.EntityContext);
    }

    [Fact]
    public void ToDocument_FactoryRoundTrip_ReconstitutesCorrectly()
    {
        var task = new AgentScheduledTask
        {
            Id = "at2",
            TaskId = "task-at2",
            Label = "Lock doors at night",
            FireAt = DateTimeOffset.UtcNow.AddHours(1),
            Prompt = "lock the front door",
            TargetAgentId = null,
            EntityContext = null
        };

        var doc = task.ToDocument();
        var reconstituted = ScheduledTaskFactory.FromDocument(doc);

        Assert.NotNull(reconstituted);
        var agentTask = Assert.IsType<AgentScheduledTask>(reconstituted);
        Assert.Equal(task.Id, agentTask.Id);
        Assert.Equal(task.TaskId, agentTask.TaskId);
        Assert.Equal(task.Label, agentTask.Label);
        Assert.Equal(task.FireAt, agentTask.FireAt);
        Assert.Equal(task.Prompt, agentTask.Prompt);
        Assert.Null(agentTask.TargetAgentId);
        Assert.Null(agentTask.EntityContext);
    }

    [Fact]
    public void FactoryReturnsNull_WhenPromptIsMissing()
    {
        var doc = new ScheduledTaskDocument
        {
            Id = "at3",
            TaskId = "task-at3",
            Label = "Missing prompt",
            FireAt = DateTimeOffset.UtcNow,
            TaskType = ScheduledTaskType.AgentTask,
            Prompt = null
        };

        var result = ScheduledTaskFactory.FromDocument(doc);
        Assert.Null(result);
    }

    private static AgentScheduledTask CreateTask(
        DateTimeOffset? fireAt = null,
        string prompt = "turn off the lights")
    {
        return new AgentScheduledTask
        {
            Id = "test-at",
            TaskId = "task-test-at",
            Label = "Test agent task",
            FireAt = fireAt ?? DateTimeOffset.UtcNow.AddMinutes(30),
            Prompt = prompt
        };
    }
}
