using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Tests for preserving original user text in agent dispatch messages.
/// </summary>
public sealed class AgentDispatchExecutorTests
{
    [Fact]
    public async Task HandleAsync_WithTailoredInstruction_PrefixesOriginalRequestMetadata()
    {
        // Arrange
        ChatMessage? capturedMessage = null;
        var invoker = A.Fake<IAgentInvoker>();
        A.CallTo(() => invoker.AgentId).Returns("light-agent");
        A.CallTo(() => invoker.InvokeAsync(A<ChatMessage>._, A<CancellationToken>._))
            .Invokes(call => capturedMessage = call.GetArgument<ChatMessage>(0))
            .Returns(new OrchestratorAgentResponse
            {
                AgentId = "light-agent",
                Content = "done",
                Success = true,
                ExecutionTimeMs = 1
            });

        var executor = new AgentDispatchExecutor(
            new Dictionary<string, IAgentInvoker>(StringComparer.OrdinalIgnoreCase)
            {
                ["light-agent"] = invoker
            },
            A.Fake<Microsoft.Extensions.Logging.ILogger<AgentDispatchExecutor>>(),
            Options.Create(new RouterExecutorOptions()));

        executor.SetUserMessage(new ChatMessage(ChatRole.User, "HOME ASSISTANT CONTEXT\n\nUser: schalte den keller ein"), "schalte den keller ein");

        var choice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Reasoning = "Light request",
            Confidence = 0.9,
            AgentInstructions =
            [
                new AgentInstruction
                {
                    AgentId = "light-agent",
                    Instruction = "Turn on the Keller lights."
                }
            ]
        };

        // Act
        await executor.HandleAsync(choice, A.Fake<IWorkflowContext>(), CancellationToken.None);

        // Assert
        Assert.NotNull(capturedMessage);
        Assert.Equal(ChatRole.User, capturedMessage!.Role);
        Assert.Equal("[Original request: \"schalte den keller ein\"]\nTurn on the Keller lights.", capturedMessage.Text);
    }

    /// <summary>
    /// Regression test for GitHub issue #114: when the LLM hallucinates additionalAgents
    /// without providing agentInstructions, only the primary agent should be dispatched.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AdditionalAgentsWithoutInstructions_DispatchesOnlyPrimaryAgent()
    {
        // Arrange — simulate the #114 scenario where LLM returned all agents as additionalAgents
        var invokedAgents = new List<string>();

        IAgentInvoker CreateFakeInvoker(string agentId)
        {
            var invoker = A.Fake<IAgentInvoker>();
            A.CallTo(() => invoker.AgentId).Returns(agentId);
            A.CallTo(() => invoker.InvokeAsync(A<ChatMessage>._, A<CancellationToken>._))
                .Invokes(_ => invokedAgents.Add(agentId))
                .Returns(new OrchestratorAgentResponse
                {
                    AgentId = agentId,
                    Content = "done",
                    Success = true,
                    ExecutionTimeMs = 1
                });
            return invoker;
        }

        var invokers = new Dictionary<string, IAgentInvoker>(StringComparer.OrdinalIgnoreCase)
        {
            ["light-agent"] = CreateFakeInvoker("light-agent"),
            ["climate-agent"] = CreateFakeInvoker("climate-agent"),
            ["music-agent"] = CreateFakeInvoker("music-agent"),
            ["scene-agent"] = CreateFakeInvoker("scene-agent"),
            ["timer-agent"] = CreateFakeInvoker("timer-agent"),
            ["general-assistant"] = CreateFakeInvoker("general-assistant"),
        };

        var executor = new AgentDispatchExecutor(
            invokers,
            A.Fake<Microsoft.Extensions.Logging.ILogger<AgentDispatchExecutor>>(),
            Options.Create(new RouterExecutorOptions()));

        executor.SetUserMessage(new ChatMessage(ChatRole.User, "Turn the lights off."));

        // Router returns high-confidence primary agent but hallucinates additionalAgents with NO instructions
        var choice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Reasoning = "The request directly asks to turn off lights, which maps perfectly to the light-agent.",
            Confidence = 1.0,
            AdditionalAgents = ["climate-agent", "music-agent", "scene-agent", "timer-agent", "general-assistant"],
            AgentInstructions = null // No instructions — this is the bug trigger
        };

        // Act
        var results = await executor.HandleAsync(choice, A.Fake<IWorkflowContext>(), CancellationToken.None);

        // Assert — only light-agent should have been invoked
        Assert.Single(invokedAgents);
        Assert.Equal("light-agent", invokedAgents[0]);
        Assert.Single(results);
        Assert.Equal("light-agent", results[0].AgentId);
    }

    /// <summary>
    /// Valid multi-agent dispatch: when additionalAgents have matching agentInstructions,
    /// all instructed agents should be dispatched.
    /// </summary>
    [Fact]
    public async Task HandleAsync_AdditionalAgentsWithInstructions_DispatchesAll()
    {
        // Arrange
        var invokedAgents = new List<string>();

        IAgentInvoker CreateFakeInvoker(string agentId)
        {
            var invoker = A.Fake<IAgentInvoker>();
            A.CallTo(() => invoker.AgentId).Returns(agentId);
            A.CallTo(() => invoker.InvokeAsync(A<ChatMessage>._, A<CancellationToken>._))
                .Invokes(_ => invokedAgents.Add(agentId))
                .Returns(new OrchestratorAgentResponse
                {
                    AgentId = agentId,
                    Content = "done",
                    Success = true,
                    ExecutionTimeMs = 1
                });
            return invoker;
        }

        var invokers = new Dictionary<string, IAgentInvoker>(StringComparer.OrdinalIgnoreCase)
        {
            ["light-agent"] = CreateFakeInvoker("light-agent"),
            ["music-agent"] = CreateFakeInvoker("music-agent"),
        };

        var executor = new AgentDispatchExecutor(
            invokers,
            A.Fake<Microsoft.Extensions.Logging.ILogger<AgentDispatchExecutor>>(),
            Options.Create(new RouterExecutorOptions()));

        executor.SetUserMessage(new ChatMessage(ChatRole.User, "Dim the lights and play some jazz."));

        var choice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Reasoning = "Multi-domain: lights + music.",
            Confidence = 0.95,
            AdditionalAgents = ["music-agent"],
            AgentInstructions =
            [
                new AgentInstruction { AgentId = "light-agent", Instruction = "Dim the lights." },
                new AgentInstruction { AgentId = "music-agent", Instruction = "Play some jazz." }
            ]
        };

        // Act
        var results = await executor.HandleAsync(choice, A.Fake<IWorkflowContext>(), CancellationToken.None);

        // Assert — both agents should be dispatched
        Assert.Equal(2, invokedAgents.Count);
        Assert.Contains("light-agent", invokedAgents);
        Assert.Contains("music-agent", invokedAgents);
        Assert.Equal(2, results.Count);
    }
}
