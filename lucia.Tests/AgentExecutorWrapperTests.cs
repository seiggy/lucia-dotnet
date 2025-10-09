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

namespace lucia.Tests;

public class AgentExecutorWrapperTests
{
    private static readonly ChatMessage SampleMessage = new(ChatRole.User, "Turn on the kitchen lights");

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

    private static AgentExecutorWrapper CreateWrapper(
        string agentId,
        StubAIAgent? agent = null,
        AgentCard? agentCard = null,
        StubTaskManager? taskManager = null,
        IOptions<AgentExecutorWrapperOptions>? options = null)
    {
        agent ??= new StubAIAgent();
        options ??= Options.Create(new AgentExecutorWrapperOptions());
        var services = new ServiceCollection()
            .AddSingleton<AIAgent>(agent)
            .BuildServiceProvider();

        return new AgentExecutorWrapper(
            agentId,
            services,
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
