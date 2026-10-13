using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="ITaskArchiveStore"/> provider must share.
/// </summary>
public abstract class TaskArchiveStoreContract
{
    protected abstract Task<StoreScope<ITaskArchiveStore>> CreateAsync();

    [Fact]
    public async Task ArchiveTaskAsync_And_GetArchivedTaskAsync_RoundTripMeaningfulFields()
    {
        await using var scope = await CreateAsync();

        var task = CreateTask(
            "task-1",
            "session-1",
            TaskState.Completed,
            "turn on the living room lights",
            "done",
            "scene-agent",
            DateTime.UtcNow.AddDays(-1));

        await scope.Store.ArchiveTaskAsync(task);

        var archived = await scope.Store.GetArchivedTaskAsync(task.Id);

        Assert.NotNull(archived);
        Assert.Equal(task.Id, archived.Id);
        Assert.Equal("session-1", archived.ContextId);
        Assert.Equal("Completed", archived.Status);
        Assert.Equal("scene-agent", archived.AgentIds.Single());
        Assert.Equal("turn on the living room lights", archived.UserInput);
        Assert.Equal("done", archived.FinalResponse);
        Assert.Equal(2, archived.MessageCount);
        Assert.True(await scope.Store.IsArchivedAsync(task.Id));
    }

    [Fact]
    public async Task ArchiveTaskAsync_UpsertsById_InsteadOfDuplicating()
    {
        await using var scope = await CreateAsync();

        var task = CreateTask("task-dup", "session-dup", TaskState.Completed, "hello", "done", "agent-one");
        await scope.Store.ArchiveTaskAsync(task);

        var updated = CreateTask("task-dup", "session-dup", TaskState.Failed, "hello again", "error", "agent-two");
        await scope.Store.ArchiveTaskAsync(updated);

        var archived = await scope.Store.GetArchivedTaskAsync("task-dup");
        Assert.NotNull(archived);
        Assert.Equal("Failed", archived.Status);
        Assert.Equal("error", archived.FinalResponse);

        var all = await scope.Store.ListArchivedTasksAsync(new TaskFilterCriteria { Page = 1, PageSize = 10 });
        Assert.Single(all.Items);
    }

    [Fact]
    public async Task ListArchivedTasksAsync_FilterAndPage_UseExpectedSemantics()
    {
        await using var scope = await CreateAsync();

        await scope.Store.ArchiveTaskAsync(CreateTask("task-a", "session-a", TaskState.Completed, "turn on the kitchen lights", "done", "agent-one", DateTime.UtcNow.AddDays(-5)));
        await scope.Store.ArchiveTaskAsync(CreateTask("task-b", "session-b", TaskState.Failed, "what is the weather", "error", "agent-two", DateTime.UtcNow.AddDays(-4)));
        await scope.Store.ArchiveTaskAsync(CreateTask("task-c", "session-c", TaskState.Completed, "dim the bedroom lights", "done", "agent-one", DateTime.UtcNow.AddDays(-3)));

        var byStatus = await scope.Store.ListArchivedTasksAsync(new TaskFilterCriteria { Status = "Completed", Page = 1, PageSize = 10 });
        Assert.Equal(2, byStatus.TotalCount);

        var byAgent = await scope.Store.ListArchivedTasksAsync(new TaskFilterCriteria { AgentId = "agent-two", Page = 1, PageSize = 10 });
        var agentRow = Assert.Single(byAgent.Items);
        Assert.Equal("task-b", agentRow.Id);

        var bySearch = await scope.Store.ListArchivedTasksAsync(new TaskFilterCriteria { Search = "weather", Page = 1, PageSize = 10 });
        var searchRow = Assert.Single(bySearch.Items);
        Assert.Equal("task-b", searchRow.Id);

        var paged = await scope.Store.ListArchivedTasksAsync(new TaskFilterCriteria { Page = 2, PageSize = 1 });
        Assert.Single(paged.Items);
        Assert.Equal(3, paged.TotalCount);
        Assert.Equal(2, paged.Page);
    }

    [Fact]
    public async Task GetTaskStatsAsync_AggregatesCountsByOutcomeAndAgent()
    {
        await using var scope = await CreateAsync();

        await scope.Store.ArchiveTaskAsync(CreateTask("task-s-1", "session-s-1", TaskState.Completed, "turn on lights", "done", "agent-one", DateTime.UtcNow.AddDays(-2)));
        await scope.Store.ArchiveTaskAsync(CreateTask("task-s-2", "session-s-2", TaskState.Failed, "weather query failed", "nope", "agent-two", DateTime.UtcNow.AddDays(-1)));
        await scope.Store.ArchiveTaskAsync(CreateTask("task-s-3", "session-s-3", TaskState.Canceled, "cancelled task", "stopped", "agent-one", DateTime.UtcNow.AddHours(-1)));

        var stats = await scope.Store.GetTaskStatsAsync();

        Assert.Equal(3, stats.TotalTasks);
        Assert.Equal(1, stats.CompletedCount);
        Assert.Equal(1, stats.FailedCount);
        Assert.Equal(1, stats.CanceledCount);
        Assert.Equal(2, stats.ByAgent["agent-one"]);
        Assert.Equal(1, stats.ByAgent["agent-two"]);
    }

    [Fact]
    public async Task GetArchivedTaskAsync_And_IsArchivedAsync_ReturnNullOrFalse_WhenMissing()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetArchivedTaskAsync("missing-task"));
        Assert.False(await scope.Store.IsArchivedAsync("missing-task"));
    }

    private static AgentTask CreateTask(
        string id,
        string contextId,
        TaskState state,
        string userInput,
        string response,
        string agentId,
        DateTime? createdAt = null)
    {
        var timestamp = createdAt ?? DateTime.UtcNow;
        var userMessage = new Message
        {
            Role = Role.User,
            MessageId = $"{id}-user",
            ContextId = contextId,
            Parts = [new Part { Text = userInput }],
        };

        var agentMessage = new Message
        {
            Role = Role.Agent,
            MessageId = $"{id}-agent",
            ContextId = contextId,
            Parts = [new Part { Text = response }],
            Extensions = [agentId],
        };

        return new AgentTask
        {
            Id = id,
            ContextId = contextId,
            Status = new A2A.TaskStatus
            {
                State = state,
                Timestamp = new DateTimeOffset(timestamp),
            },
            History = [userMessage, agentMessage],
        };
    }
}
