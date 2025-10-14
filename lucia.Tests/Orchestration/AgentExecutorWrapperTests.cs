using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Orchestration;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

public class AgentExecutorWrapperTests : TestBase
{
    private static readonly ChatMessage SampleMessage = new(ChatRole.User, "Turn on the kitchen lights");

    [Fact]
    public async Task HandleAsync_CreatesInitialThread_WhenNoThreadExists()
    {
        var agentId = "security-agent";
        var stubAgent = new StubAIAgent();

        var workflowContext = CreateWorkflowContext("ctx-initial");
        var wrapper = CreateWrapper(agentId: agentId, agent: stubAgent);

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.True(response.Success);
        Assert.NotNull(stubAgent.LastThread);
        Assert.Contains(workflowContext.Events, evt => evt is ExecutorCompletedEvent);

        var storedContext = (OrchestrationContext?)workflowContext.State[(AgentExecutorWrapper.StateScope, AgentExecutorWrapper.StateKey)];
        Assert.NotNull(storedContext);
        Assert.Equal("ctx-initial", storedContext!.ConversationId);
        Assert.True(storedContext.AgentThreads.ContainsKey(agentId));
    }

    [Fact]
    public async Task HandleAsync_ReusesExistingThread_WhenConversationMatches()
    {
        var agentId = "light-agent";
        var stubAgent = new StubAIAgent();
        var existingThread = stubAgent.GetNewThread();

        var orchestrationContext = new OrchestrationContext
        {
            ConversationId = "ctx-1",
            AgentThreads = { [agentId] = existingThread }
        };

        var workflowContext = CreateWorkflowContext("ctx-1", orchestrationContext);

        var wrapper = CreateWrapper(agentId: agentId, agent: stubAgent);

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.True(response.Success);
        Assert.Same(existingThread, stubAgent.LastThread);
        Assert.Contains(workflowContext.Events, evt => evt is ExecutorCompletedEvent);
    }

    [Fact]
    public async Task HandleAsync_CreatesNewThread_WhenConversationChanges()
    {
        var agentId = "music-agent";
        var stubAgent = new StubAIAgent();
        var orchestrationContext = new OrchestrationContext
        {
            ConversationId = "ctx-old",
            AgentThreads =
            {
                [agentId] = stubAgent.GetNewThread()
            }
        };

        var workflowContext = CreateWorkflowContext("ctx-new", orchestrationContext);
        var wrapper = CreateWrapper(agentId: agentId, agent: stubAgent);

        await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.NotNull(stubAgent.LastThread);
        Assert.NotSame(orchestrationContext.AgentThreads[agentId], stubAgent.LastThread);

        var storedContext = (OrchestrationContext?)workflowContext.State[(AgentExecutorWrapper.StateScope, AgentExecutorWrapper.StateKey)];
        Assert.NotNull(storedContext);
        Assert.Equal("ctx-new", storedContext!.ConversationId);
        Assert.True(storedContext.AgentThreads.ContainsKey(agentId));
    }

    [Fact]
    public async Task HandleAsync_AppendsMessagesToHistory()
    {
        var agentId = "climate-agent";
        var stubAgent = new StubAIAgent(runAsync: (_, _, _) =>
            Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Temperature adjusted."))));

        var workflowContext = CreateWorkflowContext("ctx-history");
        var wrapper = CreateWrapper(agentId: agentId, agent: stubAgent);

        await wrapper.HandleAsync(SampleMessage, workflowContext);

        var storedContext = (OrchestrationContext?)workflowContext.State[(AgentExecutorWrapper.StateScope, AgentExecutorWrapper.StateKey)];
        Assert.NotNull(storedContext);
        Assert.NotEmpty(storedContext!.History);
        Assert.Contains(storedContext.History, msg => msg.Role == ChatRole.User);
        Assert.Contains(storedContext.History, msg => msg.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task HandleAsync_TrimsHistory_WhenLimitExceeded()
    {
        var options = Options.Create(new AgentExecutorWrapperOptions
        {
            HistoryLimit = 5
        });

        var orchestrationContext = new OrchestrationContext
        {
            ConversationId = "ctx-trim",
            History =
            [
                new ChatMessage(ChatRole.User, "Message 1"),
                new ChatMessage(ChatRole.Assistant, "Response 1"),
                new ChatMessage(ChatRole.User, "Message 2"),
                new ChatMessage(ChatRole.Assistant, "Response 2"),
                new ChatMessage(ChatRole.User, "Message 3"),
                new ChatMessage(ChatRole.Assistant, "Response 3")
            ]
        };

        var workflowContext = CreateWorkflowContext("ctx-trim", orchestrationContext);
        var stubAgent = new StubAIAgent();
        var wrapper = CreateWrapper(agentId: "test-agent", agent: stubAgent, options: options);

        await wrapper.HandleAsync(SampleMessage, workflowContext);

        var storedContext = (OrchestrationContext?)workflowContext.State[(AgentExecutorWrapper.StateScope, AgentExecutorWrapper.StateKey)];
        Assert.NotNull(storedContext);
        Assert.True(storedContext!.History.Count <= options.Value.HistoryLimit);
        Assert.DoesNotContain(storedContext.History, msg => msg.Text == "Message 1");
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailureResponse_OnAgentTimeout()
    {
        var tcs = new TaskCompletionSource<AgentRunResponse>();
        var agent = new StubAIAgent(runAsync: (_, _, _) => tcs.Task);
        var options = Options.Create(new AgentExecutorWrapperOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50)
        });

        var wrapper = CreateWrapper(agentId: "climate-agent", agent: agent, options: options);
        var workflowContext = CreateWorkflowContext("ctx-timeout");

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.False(response.Success);
        Assert.Contains("timed out", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(workflowContext.Events, evt => evt is ExecutorFailedEvent);
        Assert.True(agent.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task HandleAsync_ReturnsFailureResponse_OnGeneralException()
    {
        var expectedException = new InvalidOperationException("Agent internal error");
        var agent = new StubAIAgent(runAsync: (_, _, _) => throw expectedException);

        var wrapper = CreateWrapper(agentId: "error-agent", agent: agent);
        var workflowContext = CreateWorkflowContext("ctx-error");

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.False(response.Success);
        Assert.Equal(expectedException.Message, response.ErrorMessage);
        Assert.Contains(workflowContext.Events, evt => evt is ExecutorFailedEvent);
    }

    [Fact]
    public async Task HandleAsync_ThrowsException_WhenRemoteAgentWithoutTaskManager()
    {
        var agentCard = new AgentCard { Name = "invalid-remote", Url = "https://example.com" };

        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = CreateWrapper(agentId: agentCard.Name, agentCard: agentCard, taskManager: null);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HandleAsync_UsesTaskManager_WhenAgentCardProvided()
    {
        var agentCard = new AgentCard
        {
            Name = "remote-agent",
            Url = "https://agents.example/remote"
        };

        var expectedTask = new AgentTask
        {
            Id = "task-123",
            ContextId = "ctx-remote",
            History =
            [
                new AgentMessage
                {
                    Role = MessageRole.Agent,
                    Parts =
                    {
                        new TextPart { Text = "Confirmed remote action" }
                    }
                }
            ],
            Status = new AgentTaskStatus
            {
                State = TaskState.Completed
            }
        };

        var taskManager = new StubTaskManager((message, token) => Task.FromResult<A2AResponse?>(expectedTask));
        var wrapper = CreateWrapper(agentId: agentCard.Name, agentCard: agentCard, taskManager: taskManager);
        var workflowContext = CreateWorkflowContext("ctx-remote");

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.True(response.Success);
        Assert.Equal(agentCard.Name, response.AgentId);
        Assert.Equal("Confirmed remote action", response.Content);

        var captured = taskManager.LastSendMessageParams;
        Assert.NotNull(captured);
        Assert.Equal("ctx-remote", captured!.Message.ContextId);
        Assert.Equal(agentCard.Url, captured.Message.Extensions?.FirstOrDefault());
    }

    [Fact]
    public async Task HandleAsync_HandlesAgentMessageResponse_FromTaskManager()
    {
        var agentCard = new AgentCard { Name = "remote-msg-agent", Url = "https://agents.example/msg" };

        var expectedMessage = new AgentMessage
        {
            Role = MessageRole.Agent,
            Parts = { new TextPart { Text = "Direct message response" } }
        };

        var taskManager = new StubTaskManager((_, _) => Task.FromResult<A2AResponse?>(expectedMessage));
        var wrapper = CreateWrapper(agentId: agentCard.Name, agentCard: agentCard, taskManager: taskManager);
        var workflowContext = CreateWorkflowContext("ctx-msg");

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.True(response.Success);
        Assert.Equal("Direct message response", response.Content);
    }

    [Fact]
    public async Task HandleAsync_HandlesNullResponse_FromTaskManager()
    {
        var agentCard = new AgentCard { Name = "remote-null-agent", Url = "https://agents.example/null" };

        var taskManager = new StubTaskManager((_, _) => Task.FromResult<A2AResponse?>(null));
        var wrapper = CreateWrapper(agentId: agentCard.Name, agentCard: agentCard, taskManager: taskManager);
        var workflowContext = CreateWorkflowContext("ctx-null");

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.False(response.Success);
        Assert.Equal("Remote agent returned no response.", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_HandlesFailedTaskState_FromTaskManager()
    {
        var agentCard = new AgentCard { Name = "remote-failed", Url = "https://agents.example/failed" };

        var failedTask = new AgentTask
        {
            Id = "task-failed",
            ContextId = "ctx-failed",
            Status = new AgentTaskStatus { State = TaskState.Failed }
        };

        var taskManager = new StubTaskManager((_, _) => Task.FromResult<A2AResponse?>(failedTask));
        var wrapper = CreateWrapper(agentId: agentCard.Name, agentCard: agentCard, taskManager: taskManager);
        var workflowContext = CreateWorkflowContext("ctx-failed");

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.False(response.Success);
        Assert.Contains("Failed", response.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_ExtractsTraceContext_FromWorkflowContext()
    {
        var agentCard = new AgentCard { Name = "trace-agent", Url = "https://agents.example/trace" };

        var expectedTask = new AgentTask
        {
            Id = "task-trace",
            ContextId = "ctx-trace",
            Status = new AgentTaskStatus { State = TaskState.Completed },
            History = [new AgentMessage { Parts = { new TextPart { Text = "Done" } } }]
        };

        var taskManager = new StubTaskManager((_, _) => Task.FromResult<A2AResponse?>(expectedTask));
        var wrapper = CreateWrapper(agentId: agentCard.Name, agentCard: agentCard, taskManager: taskManager);

        var traceContext = new Dictionary<string, string>
        {
            [AgentExecutorWrapper.TraceContextKeys.ConversationId] = "ctx-trace",
            [AgentExecutorWrapper.TraceContextKeys.TaskId] = "task-xyz"
        };

        var workflowContext = new RecordingWorkflowContext(traceContext);

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.True(response.Success);

        var captured = taskManager.LastSendMessageParams;
        Assert.NotNull(captured);
        Assert.Equal("ctx-trace", captured!.Message.ContextId);
        Assert.Equal("task-xyz", captured.Message.TaskId);
    }

    [Fact]
    public async Task HandleAsync_EmitsExecutorInvokedEvent()
    {
        var stubAgent = new StubAIAgent();
        var wrapper = CreateWrapper(agentId: "event-agent", agent: stubAgent);
        var workflowContext = CreateWorkflowContext("ctx-event");

        await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.Contains(workflowContext.Events, evt => evt is ExecutorInvokedEvent);
    }

    [Fact]
    public async Task HandleAsync_RecordsExecutionTime()
    {
        var stubAgent = new StubAIAgent(runAsync: async (_, _, _) =>
        {
            await Task.Delay(10);
            return new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "Completed"));
        });

        var wrapper = CreateWrapper(agentId: "timing-agent", agent: stubAgent);
        var workflowContext = CreateWorkflowContext("ctx-timing");

        var response = await wrapper.HandleAsync(SampleMessage, workflowContext);

        Assert.True(response.Success);
        Assert.True(response.ExecutionTimeMs >= 0);
    }

    [Fact]
    public async Task HandleAsync_UpdatesPreviousAgentId_InOrchestrationContext()
    {
        var agentId = "sequential-agent";
        var stubAgent = new StubAIAgent();

        var workflowContext = CreateWorkflowContext("ctx-sequential");
        var wrapper = CreateWrapper(agentId: agentId, agent: stubAgent);

        await wrapper.HandleAsync(SampleMessage, workflowContext);

        var storedContext = (OrchestrationContext?)workflowContext.State[(AgentExecutorWrapper.StateScope, AgentExecutorWrapper.StateKey)];
        Assert.NotNull(storedContext);
        Assert.Equal(agentId, storedContext!.PreviousAgentId);
    }

    private static AgentExecutorWrapper CreateWrapper(
        string agentId,
        StubAIAgent? agent = null,
        AgentCard? agentCard = null,
        StubTaskManager? taskManager = null,
        IOptions<AgentExecutorWrapperOptions>? options = null)
    {
        if (agent is null && agentCard is null)
        {
            agent = new StubAIAgent();
        }

        options ??= Options.Create(new AgentExecutorWrapperOptions());

        var services = new ServiceCollection();
        if (agent is not null)
        {
            services.AddSingleton<AIAgent>(agent);
        }
        var provider = services.BuildServiceProvider();

        return new AgentExecutorWrapper(
            agentId,
            provider,
            NullLogger<AgentExecutorWrapper>.Instance,
            options,
            agent,
            agentCard,
            taskManager,
            TimeProvider.System);
    }

    private static RecordingWorkflowContext CreateWorkflowContext(string conversationId, OrchestrationContext? state = null)
    {
        var traceContext = new Dictionary<string, string>
        {
            [AgentExecutorWrapper.TraceContextKeys.ConversationId] = conversationId,
            [AgentExecutorWrapper.TraceContextKeys.TaskId] = $"task-{conversationId}"
        };

        var initialState = state is null
            ? new Dictionary<(string Scope, string Key), object?>()
            : new Dictionary<(string Scope, string Key), object?>
            {
                [(AgentExecutorWrapper.StateScope, AgentExecutorWrapper.StateKey)] = state
            };

        return new RecordingWorkflowContext(traceContext, initialState);
    }
}
