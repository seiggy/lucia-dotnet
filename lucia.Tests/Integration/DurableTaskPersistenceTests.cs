using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Orchestration;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace lucia.Tests.Integration;

/// <summary>
/// Integration tests for US4 - Durable Task Persistence.
/// Tests validate that conversations survive process restarts via Redis persistence.
/// Uses Testcontainers to spin up Redis automatically for integration testing.
/// </summary>
public sealed class DurableTaskPersistenceTests : IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;
    private IDatabase? _redisDb;
    private ITaskManager? _taskManager;
    private ITaskStore? _taskStore;

    public async Task InitializeAsync()
    {
        // Start Redis container using Testcontainers
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redisContainer.StartAsync();

        // Connect to the containerized Redis instance
        var connectionString = _redisContainer.GetConnectionString();
        _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        _redisDb = _redis.GetDatabase();

        // Create real TaskStore and A2A's TaskManager for integration testing
        _taskStore = new lucia.Agents.Services.RedisTaskStore(_redis);
        var httpClient = new HttpClient();
        _taskManager = new TaskManager(httpClient, _taskStore);
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
        }

        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Scenario1_StartConversation_RestartHost_ResumeWithSameTaskId_RestoresContext()
    {
        // Arrange
        Skip.IfNot(_taskManager != null && _taskStore != null, "Docker is not available to run Redis container for integration tests");
        const string sessionId = "test-session-1";
        const string userMessage1 = "Turn on the bedroom lights";
        const string userMessage2 = "Now dim them to 50%";

        // Act - Part 1: Create initial conversation
        var task1 = await _taskManager.CreateTaskAsync(sessionId, taskId: null);
        var taskId = task1.Id;

        // Add first user message - properly save to task store
        var message1 = new AgentMessage
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = taskId,
            ContextId = sessionId,
            Parts = new List<Part> { new TextPart { Text = userMessage1 } }
        };
        task1.History = new List<AgentMessage> { message1 };
        await _taskStore!.SetTaskAsync(task1);  // SAVE before UpdateStatus

        // Update status to working
        await _taskManager.UpdateStatusAsync(taskId, TaskState.Working);

        // Add assistant response - must reload task first
        var taskAfterWorking = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = taskId });
        var response1 = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = taskId,
            ContextId = sessionId,
            Parts = new List<Part> { new TextPart { Text = "I've turned on the bedroom lights." } }
        };
        taskAfterWorking!.History!.Add(response1);
        await _taskStore.SetTaskAsync(taskAfterWorking);  // SAVE before UpdateStatus

        // Complete the task
        await _taskManager.UpdateStatusAsync(taskId, TaskState.Completed, response1, final: true);

        // Simulate host restart by disposing and recreating TaskManager
        var httpClient = new HttpClient();
        _taskManager = new TaskManager(httpClient, _taskStore!);

        // Act - Part 2: Resume conversation with same taskId
        var taskQueryParams = new TaskQueryParams { Id = taskId };
        var restoredTask = await _taskManager.GetTaskAsync(taskQueryParams);

        // Assert - Task was restored from Redis
        Assert.NotNull(restoredTask);
        Assert.Equal(taskId, restoredTask.Id);
        Assert.Equal(sessionId, restoredTask.ContextId);
        Assert.NotNull(restoredTask.History);
        Assert.Equal(2, restoredTask.History.Count);
        
        // Verify first message
        Assert.Equal(MessageRole.User, restoredTask.History[0].Role);
        Assert.Equal(userMessage1, ((TextPart)restoredTask.History[0].Parts[0]).Text);
        
        // Verify assistant response
        Assert.Equal(MessageRole.Agent, restoredTask.History[1].Role);
        Assert.Equal("I've turned on the bedroom lights.", ((TextPart)restoredTask.History[1].Parts[0]).Text);

        // Act - Part 3: Add follow-up message to restored conversation
        var message2 = new AgentMessage
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = taskId,
            ContextId = sessionId,
            Parts = new List<Part> { new TextPart { Text = userMessage2 } }
        };
        restoredTask.History!.Add(message2);
        await _taskStore.SetTaskAsync(restoredTask);  // SAVE before UpdateStatus

        await _taskManager.UpdateStatusAsync(taskId, TaskState.Working);

        // Reload task to get fresh copy after status update
        var taskAfterWorking2 = await _taskManager.GetTaskAsync(taskQueryParams);
        var response2 = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = taskId,
            ContextId = sessionId,
            Parts = new List<Part> { new TextPart { Text = "I've dimmed the bedroom lights to 50%." } }
        };
        taskAfterWorking2!.History!.Add(response2);
        await _taskStore.SetTaskAsync(taskAfterWorking2);  // SAVE before UpdateStatus

        await _taskManager.UpdateStatusAsync(taskId, TaskState.Completed, response2, final: true);

        // Assert - Conversation continued with full context
        var finalTask = await _taskManager.GetTaskAsync(taskQueryParams);
        Assert.NotNull(finalTask);
        Assert.Equal(4, finalTask.History!.Count);
        Assert.Equal(userMessage2, ((TextPart)finalTask.History[2].Parts[0]).Text);
    }

    [SkippableFact]
    public async Task Scenario2_MultipleActiveConversations_RestartHost_AllContextsAvailable()
    {
        // Arrange
        Skip.IfNot(_taskManager != null && _taskStore != null, "Docker is not available to run Redis container for integration tests");
        const string sessionId = "test-session-multi";
        
        // Create 3 different conversations
        var task1 = await _taskManager.CreateTaskAsync(sessionId, taskId: null);
        var task2 = await _taskManager.CreateTaskAsync(sessionId, taskId: null);
        var task3 = await _taskManager.CreateTaskAsync(sessionId, taskId: null);

        // Add messages to each task
        task1.History = new List<AgentMessage>
        {
            new() { 
                Role = MessageRole.User, 
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = task1.Id,
                ContextId = sessionId,
                Parts = new List<Part> { new TextPart { Text = "Turn on living room lights" } }
            }
        };
        await _taskStore.SetTaskAsync(task1);  // SAVE before UpdateStatus
        await _taskManager.UpdateStatusAsync(task1.Id, TaskState.Working);

        task2.History = new List<AgentMessage>
        {
            new() { 
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = task2.Id,
                ContextId = sessionId,
                Parts = new List<Part> { new TextPart { Text = "Play jazz music" } }
            }
        };
        await _taskStore.SetTaskAsync(task2);  // SAVE before UpdateStatus
        await _taskManager.UpdateStatusAsync(task2.Id, TaskState.Working);

        task3.History = new List<AgentMessage>
        {
            new() { 
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = task3.Id,
                ContextId = sessionId,
                Parts = new List<Part> { new TextPart { Text = "Set temperature to 72" } }
            }
        };
        await _taskStore.SetTaskAsync(task3);  // SAVE before UpdateStatus
        await _taskManager.UpdateStatusAsync(task3.Id, TaskState.Working);

        // Simulate host restart
        var httpClient = new HttpClient();
        _taskManager = new TaskManager(httpClient, _taskStore!);

        // Act - Retrieve all tasks
        var restored1 = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task1.Id });
        var restored2 = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task2.Id });
        var restored3 = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task3.Id });

        // Assert - All tasks restored with correct content
        Assert.NotNull(restored1);
        Assert.Equal("Turn on living room lights", ((TextPart)restored1.History?.FirstOrDefault()?.Parts[0]!).Text);

        Assert.NotNull(restored2);
        Assert.Equal("Play jazz music", ((TextPart)restored2.History?.FirstOrDefault()?.Parts[0]!).Text);

        Assert.NotNull(restored3);
        Assert.Equal("Set temperature to 72", ((TextPart)restored3.History?.FirstOrDefault()?.Parts[0]!).Text);
    }

    [SkippableFact]
    public async Task Scenario3_ExpiredTTL_GracefulNewConversationStart()
    {
        // Arrange
        Skip.IfNot(_taskManager != null && _taskStore != null, "Docker is not available to run Redis container for integration tests");
        
        const string sessionId = "test-session-expired";
        const string expiredTaskId = "expired-task-id";

        // Create a task but don't persist it (simulating expired task)
        // In a real scenario, we'd wait 24 hours or manually expire the key
        
        // Act - Try to get a task that doesn't exist (expired)
        var expiredTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = expiredTaskId });

        // Assert - Task not found (expired)
        Assert.Null(expiredTask);

        // Act - Create new conversation gracefully when old one is expired
        var newTask = await _taskManager.CreateTaskAsync(sessionId, taskId: null);
        Assert.NotNull(newTask);
        Assert.NotEqual(expiredTaskId, newTask.Id); // New ID assigned

        newTask.History = new List<AgentMessage>
        {
            new() { 
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = newTask.Id,
                ContextId = sessionId,
                Parts = new List<Part> { new TextPart { Text = "Start new conversation" } }
            }
        };
        await _taskStore.SetTaskAsync(newTask);  // SAVE before UpdateStatus

        await _taskManager.UpdateStatusAsync(newTask.Id, TaskState.Working);

        // Assert - New conversation starts without errors
        var retrievedTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = newTask.Id });
        Assert.NotNull(retrievedTask);
        Assert.Single(retrievedTask.History!);
        Assert.Equal("Start new conversation", ((TextPart)retrievedTask.History[0].Parts[0]).Text);
    }

    [SkippableFact]
    public async Task TaskPersistence_MaintainsA2AProtocolCompliance()
    {
        // Arrange
        Skip.IfNot(_taskManager != null && _taskStore != null, "Docker is not available to run Redis container for integration tests");
        const string sessionId = "test-a2a-compliance";

        // Act - Create task and verify A2A structure
        var task = await _taskManager.CreateTaskAsync(sessionId, taskId: null);
        
        // Assert - Task has required A2A fields
        Assert.NotNull(task.Id);
        Assert.NotNull(task.ContextId);
        Assert.Equal(sessionId, task.ContextId);
        Assert.NotNull(task.Status);
        Assert.Equal(TaskState.Submitted, task.Status.State);

        // Act - Update with full A2A lifecycle
        task.History = new List<AgentMessage>
        {
            new() 
            { 
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = task.Id,
                ContextId = sessionId,
                Parts = new List<Part> { new TextPart { Text = "Test message" } }
            }
        };

        // Transition: Submitted → Working
        await _taskManager.UpdateStatusAsync(task.Id, TaskState.Working);
        var workingTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(workingTask);
        Assert.Equal(TaskState.Working, workingTask.Status.State);

        // Transition: Working → Completed
        var finalMessage = new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            TaskId = task.Id,
            ContextId = sessionId,
            Parts = new List<Part> { new TextPart { Text = "Task completed" } }
        };
        await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed, finalMessage, final: true);

        var completedTask = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = task.Id });
        Assert.NotNull(completedTask);
        Assert.Equal(TaskState.Completed, completedTask.Status.State);
        
        // Verify A2A message structure
        Assert.NotNull(completedTask.History);
        Assert.All(completedTask.History, msg =>
        {
            Assert.NotNull(msg.Role);
            Assert.NotNull(msg.Parts);
            Assert.NotEmpty(msg.Parts);
        });
    }

    [SkippableFact]
    public async Task ContextRestoration_SuccessRate_MeetsSuccessCriterion()
    {
        // Arrange - Test SC-005: 99% context restoration success rate
        Skip.IfNot(_taskManager != null && _taskStore != null, "Docker is not available to run Redis container for integration tests");
        const int totalTests = 100;
        const double requiredSuccessRate = 0.99;
        int successfulRestorations = 0;

        var taskIds = new List<string>();

        // Act - Create and persist multiple tasks
        for (int i = 0; i < totalTests; i++)
        {
            var task = await _taskManager.CreateTaskAsync($"session-{i}", taskId: null);
            taskIds.Add(task.Id);

            task.History = new List<AgentMessage>
            {
                new() 
                { 
                    Role = MessageRole.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    TaskId = task.Id,
                    ContextId = $"session-{i}",
                    Parts = new List<Part> { new TextPart { Text = $"Test message {i}" } }
                }
            };
            await _taskStore.SetTaskAsync(task);  // SAVE before UpdateStatus

            await _taskManager.UpdateStatusAsync(task.Id, TaskState.Completed);
        }

        // Simulate restart
        var httpClient = new HttpClient();
        _taskManager = new TaskManager(httpClient, _taskStore!);

        // Restore all tasks
        foreach (var taskId in taskIds)
        {
            var restored = await _taskManager.GetTaskAsync(new TaskQueryParams { Id = taskId });
            if (restored != null && restored.History?.Count > 0)
            {
                successfulRestorations++;
            }
        }

        // Assert - Success rate meets SC-005 requirement
        double actualSuccessRate = (double)successfulRestorations / totalTests;
        Assert.True(actualSuccessRate >= requiredSuccessRate, 
            $"Context restoration success rate {actualSuccessRate:P} does not meet requirement of {requiredSuccessRate:P}");
    }
}
