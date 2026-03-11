using FakeItEasy;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Tests for the personality prompt feature in ResultAggregatorExecutor.
/// Verifies that when personality instructions and an IChatClient are provided,
/// the aggregator rewrites the composed message through the LLM; otherwise,
/// it returns the raw composed message unchanged.
/// </summary>
public sealed class PersonalityPromptTests : TestBase
{
    [Fact]
    public async Task HandleAsync_WithoutPersonality_ReturnsRawComposedMessage()
    {
        // Arrange — no personality (both null)
        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()));

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Turned on the living room lights.")
                .Build()
        };

        var context = A.Fake<IWorkflowContext>();

        // Act
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert — raw message returned unchanged
        Assert.Equal("Turned on the living room lights.", result.Text);
    }

    [Fact]
    public async Task HandleAsync_WithPersonality_CallsLlmAndReturnsRewrittenMessage()
    {
        // Arrange — personality enabled
        var stubClient = new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "Ahoy matey! Yer living room lights be shinin' bright!")])
        ]);

        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stubClient,
            personalityInstructions: "You are a pirate. Rewrite all responses in pirate speak.");

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Turned on the living room lights.")
                .Build()
        };

        var context = A.Fake<IWorkflowContext>();

        // Act
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert — rewritten message returned
        Assert.Equal("Ahoy matey! Yer living room lights be shinin' bright!", result.Text);
        Assert.Equal(1, stubClient.InvocationCount);

        // Verify the system message contains personality instructions
        var captured = stubClient.CapturedMessages[0];
        Assert.Equal(2, captured.Count);
        Assert.Equal(ChatRole.System, captured[0].Role);
        Assert.Contains("pirate", captured[0].Text ?? "");
        Assert.Equal(ChatRole.User, captured[1].Role);
        Assert.Contains("living room lights", captured[1].Text ?? "");
    }

    [Fact]
    public async Task HandleAsync_WithPersonality_LlmFails_FallsBackToRawMessage()
    {
        // Arrange — personality enabled but LLM throws
        var stubClient = new StubChatClient(
        [
            _ => throw new InvalidOperationException("LLM service unavailable")
        ]);

        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stubClient,
            personalityInstructions: "You are a pirate.");

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Turned on the kitchen lights.")
                .Build()
        };

        var context = A.Fake<IWorkflowContext>();

        // Act — should not throw
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert — falls back to raw composed message
        Assert.Equal("Turned on the kitchen lights.", result.Text);
    }

    [Fact]
    public async Task HandleAsync_WithInstructionsButNullChatClient_ReturnsRawMessage()
    {
        // Arrange — instructions set but chatClient is null (defensive)
        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: null,
            personalityInstructions: "You are a pirate.");

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Turned on the bedroom lights.")
                .Build()
        };

        var context = A.Fake<IWorkflowContext>();

        // Act
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert — raw message returned since chatClient is null
        Assert.Equal("Turned on the bedroom lights.", result.Text);
    }

    [Fact]
    public async Task HandleAsync_WithPersonality_MultiAgentResponses_ConcatenatesBeforeRewrite()
    {
        // Arrange — multiple agent responses should be composed first, then rewritten
        var stubClient = new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "Everything is set up perfectly, captain!")])
        ]);

        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stubClient,
            personalityInstructions: "Be enthusiastic.");

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Turned on the lights.")
                .Build(),
            new AgentResponseBuilder()
                .WithAgentId("music-agent")
                .WithContent("Playing jazz music.")
                .Build()
        };

        var context = A.Fake<IWorkflowContext>();

        // Act
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert — rewritten message returned
        Assert.Equal("Everything is set up perfectly, captain!", result.Text);

        // Verify the user message sent to LLM contains both agent responses
        var userMessage = stubClient.CapturedMessages[0][1].Text ?? "";
        Assert.Contains("lights", userMessage);
        Assert.Contains("jazz", userMessage);
    }

    [Fact]
    public async Task HandleAsync_WithPersonality_LlmReturnsEmpty_FallsBackToRawMessage()
    {
        // Arrange — LLM returns empty/whitespace response
        var stubClient = new StubChatClient(
        [
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "   ")])
        ]);

        var aggregator = new ResultAggregatorExecutor(
            CreateLogger<ResultAggregatorExecutor>(),
            CreateOptions(new ResultAggregatorOptions()),
            personalityChatClient: stubClient,
            personalityInstructions: "Be concise.");

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Turned on the porch lights.")
                .Build()
        };

        var context = A.Fake<IWorkflowContext>();

        // Act
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert — falls back to raw composed message
        Assert.Equal("Turned on the porch lights.", result.Text);
    }
}
