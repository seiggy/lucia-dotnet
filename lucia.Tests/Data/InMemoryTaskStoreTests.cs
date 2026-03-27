using A2A;
using FakeItEasy;
using Microsoft.Extensions.Logging;

using TaskStore = lucia.Data.InMemory.InMemoryTaskStore;

namespace lucia.Tests.Data;

public sealed class InMemoryTaskStoreTests : IDisposable
{
    private readonly TaskStore _store;

    public InMemoryTaskStoreTests()
    {
        var logger = A.Fake<ILogger<TaskStore>>();
        _store = new TaskStore(logger);
    }

    [Fact]
    public async Task SaveTaskAsync_And_GetTaskAsync_RoundTrips()
    {
        var task = new AgentTask
        {
            Id = "task-1",
            Status = new A2A.TaskStatus { State = TaskState.Submitted }
        };

        await _store.SaveTaskAsync(task.Id, task);

        var result = await _store.GetTaskAsync("task-1");

        Assert.NotNull(result);
        Assert.Equal("task-1", result.Id);
        Assert.Equal(TaskState.Submitted, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsNull_ForUnknownTaskId()
    {
        var result = await _store.GetTaskAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatus_ViaLoadModifySave_UpdatesStateAndAppendsMessage()
    {
        var task = new AgentTask
        {
            Id = "task-2",
            Status = new A2A.TaskStatus { State = TaskState.Submitted }
        };
        await _store.SaveTaskAsync(task.Id, task);

        var message = new Message
        {
            Role = A2A.Role.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            Parts = new List<Part> { new Part { Text = "Working on it" } }
        };

        // Load, modify status + history, save
        var loaded = await _store.GetTaskAsync("task-2");
        Assert.NotNull(loaded);
        loaded.Status = new A2A.TaskStatus { State = TaskState.Working, Message = message };
        loaded.History ??= [];
        loaded.History.Add(message);
        await _store.SaveTaskAsync(loaded.Id, loaded);

        var retrieved = await _store.GetTaskAsync("task-2");
        Assert.NotNull(retrieved);
        Assert.Equal(TaskState.Working, retrieved.Status.State);
        Assert.NotNull(retrieved.History);
        Assert.Single(retrieved.History);
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsNull_ForNonexistentTask()
    {
        var result = await _store.GetTaskAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteTaskAsync_RemovesTask()
    {
        var task = new AgentTask
        {
            Id = "task-delete",
            Status = new A2A.TaskStatus { State = TaskState.Submitted }
        };
        await _store.SaveTaskAsync(task.Id, task);

        await _store.DeleteTaskAsync("task-delete");

        var result = await _store.GetTaskAsync("task-delete");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllTrackedTaskIdsAsync_ReturnsAllStoredIds()
    {
        var task1 = new AgentTask
        {
            Id = "tracked-1",
            Status = new A2A.TaskStatus { State = TaskState.Submitted }
        };
        await _store.SaveTaskAsync(task1.Id, task1);
        var task2 = new AgentTask
        {
            Id = "tracked-2",
            Status = new A2A.TaskStatus { State = TaskState.Working }
        };
        await _store.SaveTaskAsync(task2.Id, task2);

        var ids = await _store.GetAllTrackedTaskIdsAsync();

        Assert.Contains("tracked-1", ids);
        Assert.Contains("tracked-2", ids);
    }

    [Fact]
    public async Task RemoveTaskIdAsync_RemovesFromIndex()
    {
        var taskToRemove = new AgentTask
        {
            Id = "remove-me",
            Status = new A2A.TaskStatus { State = TaskState.Submitted }
        };
        await _store.SaveTaskAsync(taskToRemove.Id, taskToRemove);

        await _store.RemoveTaskIdAsync("remove-me");

        var ids = await _store.GetAllTrackedTaskIdsAsync();
        Assert.DoesNotContain("remove-me", ids);
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
