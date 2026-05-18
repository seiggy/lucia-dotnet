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
}
