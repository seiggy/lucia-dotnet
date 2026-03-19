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
    public async Task SetTaskAsync_And_GetTaskAsync_RoundTrips()
    {
        var task = new AgentTask
        {
            Id = "task-1",
            Status = new AgentTaskStatus { State = TaskState.Submitted }
        };

        await _store.SetTaskAsync(task);

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
    public async Task UpdateStatusAsync_UpdatesStateAndAppendsMessage()
    {
        var task = new AgentTask
        {
            Id = "task-2",
            Status = new AgentTaskStatus { State = TaskState.Submitted }
        };
        await _store.SetTaskAsync(task);

        var message = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            Parts = new List<Part> { new TextPart { Text = "Working on it" } }
        };

        var updatedStatus = await _store.UpdateStatusAsync("task-2", TaskState.Working, message);

        Assert.Equal(TaskState.Working, updatedStatus.State);

        var retrieved = await _store.GetTaskAsync("task-2");
        Assert.NotNull(retrieved);
        Assert.Equal(TaskState.Working, retrieved.Status.State);
        Assert.NotNull(retrieved.History);
        Assert.Single(retrieved.History);
    }

    [Fact]
    public async Task UpdateStatusAsync_ThrowsForUnknownTask()
    {
        await Assert.ThrowsAsync<A2AException>(
            () => _store.UpdateStatusAsync("nonexistent", TaskState.Working));
    }

    [Fact]
    public async Task SetPushNotificationConfigAsync_And_GetPushNotificationAsync_RoundTrips()
    {
        var config = new TaskPushNotificationConfig { TaskId = "task-3" };

        await _store.SetPushNotificationConfigAsync(config);

        var result = await _store.GetPushNotificationAsync("task-3", "default");

        Assert.NotNull(result);
        Assert.Equal("task-3", result.TaskId);
    }

    [Fact]
    public async Task GetPushNotificationsAsync_ReturnsAllConfigsForTask()
    {
        var config = new TaskPushNotificationConfig { TaskId = "task-4" };
        await _store.SetPushNotificationConfigAsync(config);

        var results = await _store.GetPushNotificationsAsync("task-4");

        Assert.Single(results);
    }

    [Fact]
    public async Task GetAllTrackedTaskIdsAsync_ReturnsAllStoredIds()
    {
        await _store.SetTaskAsync(new AgentTask
        {
            Id = "tracked-1",
            Status = new AgentTaskStatus { State = TaskState.Submitted }
        });
        await _store.SetTaskAsync(new AgentTask
        {
            Id = "tracked-2",
            Status = new AgentTaskStatus { State = TaskState.Working }
        });

        var ids = await _store.GetAllTrackedTaskIdsAsync();

        Assert.Contains("tracked-1", ids);
        Assert.Contains("tracked-2", ids);
    }

    [Fact]
    public async Task RemoveTaskIdAsync_RemovesFromIndex()
    {
        await _store.SetTaskAsync(new AgentTask
        {
            Id = "remove-me",
            Status = new AgentTaskStatus { State = TaskState.Submitted }
        });

        await _store.RemoveTaskIdAsync("remove-me");

        var ids = await _store.GetAllTrackedTaskIdsAsync();
        Assert.DoesNotContain("remove-me", ids);
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
