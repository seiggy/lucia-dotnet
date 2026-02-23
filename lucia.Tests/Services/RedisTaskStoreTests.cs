using A2A;
using FakeItEasy;
using lucia.Agents.Services;
using StackExchange.Redis;
using System.Text.Json;

namespace lucia.Tests.Services;

public class RedisTaskStoreTests
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly RedisTaskStore _store;

    public RedisTaskStoreTests()
    {
        _redis = A.Fake<IConnectionMultiplexer>();
        _database = A.Fake<IDatabase>();
        A.CallTo(() => _redis.GetDatabase(A<int>._, A<object>._)).Returns(_database);
        _store = new RedisTaskStore(_redis);
    }

    [Fact]
    public async Task GetTaskAsync_WhenTaskExists_ReturnsAgentTask()
    {
        // Arrange
        var taskId = "task-123";
        var agentTask = new AgentTask
        {
            Id = taskId,
            ContextId = "context-456",
            Status = new AgentTaskStatus { State = TaskState.Working }
        };
        var json = JsonSerializer.Serialize(agentTask);
        
        A.CallTo(() => _database.StringGetAsync("lucia:task:task-123", A<CommandFlags>._))
            .Returns(new RedisValue(json));

        // Act
        var result = await _store.GetTaskAsync(taskId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskId, result.Id);
        Assert.Equal("context-456", result.ContextId);
        Assert.Equal(TaskState.Working, result.Status.State);
    }

    [Fact]
    public async Task GetTaskAsync_WhenTaskDoesNotExist_ReturnsNull()
    {
        // Arrange
        var taskId = "nonexistent-task";
        A.CallTo(() => _database.StringGetAsync("lucia:task:nonexistent-task", A<CommandFlags>._))
            .Returns(RedisValue.Null);

        // Act
        var result = await _store.GetTaskAsync(taskId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetTaskAsync_PersistsTaskWithTtl()
    {
        // Arrange
        var agentTask = new AgentTask
        {
            Id = "task-789",
            ContextId = "context-abc",
            Status = new AgentTaskStatus { State = TaskState.Submitted },
            History = new List<AgentMessage>
            {
                new()
                {
                    MessageId = "msg-1",
                    Role = MessageRole.User,
                    Parts = new List<Part> { new TextPart { Text = "Turn on lights" } }
                }
            }
        };

        // Act
        await _store.SetTaskAsync(agentTask);

        // Assert
        A.CallTo(() => _database.StringSetAsync(
            "lucia:task:task-789",
            A<RedisValue>.That.Matches(v => v.ToString().Contains("task-789")),
            A<TimeSpan>.That.Matches(t => t == TimeSpan.FromHours(24)),
            A<bool>._,
            A<When>._,
            A<CommandFlags>._))
            .MustHaveHappenedOnceExactly();
        
        // Verify task ID was added to the index set
        A.CallTo(() => _database.SetAddAsync(
            "lucia:task-ids",
            A<RedisValue>.That.Matches(v => v.ToString() == "task-789"),
            CommandFlags.FireAndForget))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenTaskExists_UpdatesStatusAndReturnsNew()
    {
        // Arrange
        var taskId = "task-update-123";
        var existingTask = new AgentTask
        {
            Id = taskId,
            ContextId = "context-999",
            Status = new AgentTaskStatus { State = TaskState.Working }
        };
        var existingJson = JsonSerializer.Serialize(existingTask);
        
        A.CallTo(() => _database.StringGetAsync($"lucia:task:{taskId}", A<CommandFlags>._))
            .Returns(new RedisValue(existingJson));

        var message = new AgentMessage
        {
            MessageId = "status-msg",
            Role = MessageRole.Agent,
            Parts = new List<Part> { new TextPart { Text = "Processing complete" } }
        };

        // Act
        var result = await _store.UpdateStatusAsync(taskId, TaskState.Completed, message);

        // Assert
        Assert.Equal(TaskState.Completed, result.State);
        Assert.NotNull(result.Message);
        Assert.Equal("status-msg", result.Message.MessageId);
        
        // Verify task was saved back to Redis
        A.CallTo(() => _database.StringSetAsync(
            $"lucia:task:{taskId}",
            A<RedisValue>._,
            A<TimeSpan>._,
            A<bool>._,
            A<When>._,
            A<CommandFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateStatusAsync_WhenTaskDoesNotExist_ThrowsA2AException()
    {
        // Arrange
        var taskId = "nonexistent-task";
        A.CallTo(() => _database.StringGetAsync($"lucia:task:{taskId}", A<CommandFlags>._))
            .Returns(RedisValue.Null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<A2AException>(
            () => _store.UpdateStatusAsync(taskId, TaskState.Completed));
        
        Assert.Equal(A2AErrorCode.TaskNotFound, exception.ErrorCode);
        Assert.Contains(taskId, exception.Message);
    }

    [Fact]
    public async Task GetPushNotificationAsync_WhenConfigExists_ReturnsConfig()
    {
        // Arrange
        var taskId = "task-notif-123";
        var notificationId = "notif-456";
        var config = new TaskPushNotificationConfig
        {
            TaskId = taskId
        };
        var json = JsonSerializer.Serialize(config);
        
        A.CallTo(() => _database.StringGetAsync(
            $"lucia:task:{taskId}:notification:{notificationId}",
            A<CommandFlags>._))
            .Returns(new RedisValue(json));

        // Act
        var result = await _store.GetPushNotificationAsync(taskId, notificationId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(taskId, result.TaskId);
    }

    [Fact]
    public async Task GetPushNotificationAsync_WhenConfigDoesNotExist_ReturnsNull()
    {
        // Arrange
        var taskId = "task-no-notif";
        var notificationId = "notif-missing";
        
        A.CallTo(() => _database.StringGetAsync(
            $"lucia:task:{taskId}:notification:{notificationId}",
            A<CommandFlags>._))
            .Returns(RedisValue.Null);

        // Act
        var result = await _store.GetPushNotificationAsync(taskId, notificationId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SetPushNotificationConfigAsync_PersistsConfigWithTtl()
    {
        // Arrange
        var config = new TaskPushNotificationConfig
        {
            TaskId = "task-notif-789"
        };

        // Act
        await _store.SetPushNotificationConfigAsync(config);

        // Assert
        A.CallTo(() => _database.StringSetAsync(
            "lucia:task:task-notif-789:notification:default",
            A<RedisValue>.That.Matches(v => v.ToString().Contains("task-notif-789")),
            A<TimeSpan>.That.Matches(t => t == TimeSpan.FromHours(24)),
            A<bool>._,
            A<When>._,
            A<CommandFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetTaskAsync_VerifiesCorrectRedisKeyPattern()
    {
        // Arrange
        var taskId = "my-task-id";
        A.CallTo(() => _database.StringGetAsync(A<RedisKey>._, A<CommandFlags>._))
            .Returns(RedisValue.Null);

        // Act
        await _store.GetTaskAsync(taskId);

        // Assert
        A.CallTo(() => _database.StringGetAsync(
            A<RedisKey>.That.Matches(k => k.ToString() == "lucia:task:my-task-id"),
            A<CommandFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenRedisIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RedisTaskStore(null!));
    }

    [Fact]
    public async Task SetTaskAsync_SerializesWithCamelCaseNaming()
    {
        // Arrange
        var agentTask = new AgentTask
        {
            Id = "task-camel",
            ContextId = "context-camel",
            Status = new AgentTaskStatus { State = TaskState.Working }
        };

        RedisValue capturedValue = default;
        A.CallTo(() => _database.StringSetAsync(
            A<RedisKey>._,
            A<RedisValue>._,
            A<TimeSpan?>._,
            A<bool>._,
            A<When>._,
            A<CommandFlags>._))
            .Invokes(call => capturedValue = call.GetArgument<RedisValue>(1))
            .Returns(true);

        // Act
        await _store.SetTaskAsync(agentTask);

        // Assert
        var json = capturedValue.ToString();
        Assert.Contains("\"id\":", json);  // camelCase
        Assert.Contains("\"contextId\":", json);  // camelCase
        Assert.Contains("\"status\":", json);  // camelCase
        Assert.DoesNotContain("\"Id\":", json);  // Not PascalCase
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesTimestamp()
    {
        // Arrange
        var taskId = "task-timestamp";
        var existingTask = new AgentTask
        {
            Id = taskId,
            ContextId = "context-ts",
            Status = new AgentTaskStatus 
            { 
                State = TaskState.Working,
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
        };
        var existingJson = JsonSerializer.Serialize(existingTask);
        
        A.CallTo(() => _database.StringGetAsync($"lucia:task:{taskId}", A<CommandFlags>._))
            .Returns(new RedisValue(existingJson));

        var beforeUpdate = DateTimeOffset.UtcNow;

        // Act
        var result = await _store.UpdateStatusAsync(taskId, TaskState.Completed);

        // Assert
        Assert.True(result.Timestamp >= beforeUpdate);
        Assert.True(result.Timestamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetPushNotificationsAsync_WhenMultipleExist_ReturnsAll()
    {
        // Arrange
        var taskId = "task-multi-notif";
        var server = A.Fake<IServer>();
        var endpoints = new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) };
        
        A.CallTo(() => _redis.GetEndPoints(A<bool>._)).Returns(endpoints);
        A.CallTo(() => _redis.GetServer(A<System.Net.EndPoint>._, A<object>._)).Returns(server);

        var keys = new List<RedisKey>
        {
            new RedisKey($"lucia:task:{taskId}:notification:notif-1"),
            new RedisKey($"lucia:task:{taskId}:notification:notif-2")
        };
        
        A.CallTo(() => server.Keys(A<int>._, A<RedisValue>._, A<int>._, A<long>._, A<int>._, A<CommandFlags>._))
            .Returns(keys);

        var config1 = new TaskPushNotificationConfig { TaskId = taskId };
        var config2 = new TaskPushNotificationConfig { TaskId = taskId };
        
        A.CallTo(() => _database.StringGetAsync(keys[0], A<CommandFlags>._))
            .Returns(new RedisValue(JsonSerializer.Serialize(config1)));
        A.CallTo(() => _database.StringGetAsync(keys[1], A<CommandFlags>._))
            .Returns(new RedisValue(JsonSerializer.Serialize(config2)));

        // Act
        var result = (await _store.GetPushNotificationsAsync(taskId)).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal(taskId, c.TaskId));
    }

    [Fact]
    public async Task GetPushNotificationsAsync_WhenNoneExist_ReturnsEmpty()
    {
        // Arrange
        var taskId = "task-no-notifs";
        var server = A.Fake<IServer>();
        var endpoints = new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) };
        
        A.CallTo(() => _redis.GetEndPoints(A<bool>._)).Returns(endpoints);
        A.CallTo(() => _redis.GetServer(A<System.Net.EndPoint>._, A<object>._)).Returns(server);
        
        A.CallTo(() => server.Keys(A<int>._, A<RedisValue>._, A<int>._, A<long>._, A<int>._, A<CommandFlags>._))
            .Returns(Enumerable.Empty<RedisKey>());

        // Act
        var result = await _store.GetPushNotificationsAsync(taskId);

        // Assert
        Assert.Empty(result);
    }
}
