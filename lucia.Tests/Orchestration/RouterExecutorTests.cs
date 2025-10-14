using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FakeItEasy;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Registry;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using AgentCard = A2A.AgentCard;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Tests for RouterExecutor routing logic and LLM integration.
/// </summary>
public class RouterExecutorTests : TestBase
{
    private readonly IChatClient _fakeChatClient;
    private readonly AgentRegistry _fakeRegistry;
    private readonly ILogger<RouterExecutor> _logger;
    private readonly RouterExecutorOptions _options;
    private readonly IWorkflowContext _fakeContext;

    public RouterExecutorTests()
    {
        _fakeChatClient = A.Fake<IChatClient>();
        _fakeRegistry = A.Fake<AgentRegistry>();
        _logger = CreateLogger<RouterExecutor>();
        _fakeContext = A.Fake<IWorkflowContext>();
        
        _options = new RouterExecutorOptions
        {
            ConfidenceThreshold = 0.7,
            MaxAttempts = 3,
            Temperature = 0.3,
            MaxOutputTokens = 500,
            IncludeAgentCapabilities = true,
            IncludeSkillExamples = true
        };
    }

    [Fact]
    public async Task HandleAsync_WithHighConfidenceSelection_ReturnsAgentChoiceResult()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder()
                .WithName("light-agent")
                .WithDescription("Controls lighting devices and scenes")
                .Build()
        };

        var expectedResult = new AgentChoiceResultBuilder()
            .WithAgentId("light-agent")
            .WithConfidence(0.95)
            .WithReasoning("User clearly wants to control lights")
            .Build();

        SetupAgentRegistry(availableAgents);
        SetupChatClientResponse(expectedResult);

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn on the kitchen lights");

        // Act
        var result = await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("light-agent", result.AgentId);
        Assert.Equal(0.95, result.Confidence);
        Assert.Equal("User clearly wants to control lights", result.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_WithLowConfidence_ReturnsClarificationAgent()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder()
                .WithName("light-agent")
                .WithDescription("Controls lighting devices and scenes")
                .Build(),
            new AgentCardBuilder()
                .WithName("music-agent")
                .WithDescription("Controls music playback")
                .Build()
        };

        var lowConfidenceResult = new AgentChoiceResultBuilder()
            .WithAgentId("light-agent")
            .WithConfidence(0.5)
            .WithReasoning("Request is ambiguous")
            .Build();

        SetupAgentRegistry(availableAgents);
        SetupChatClientResponse(lowConfidenceResult);

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn it on");

        // Act
        var result = await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("clarification", result.AgentId);
        Assert.Equal(0.5, result.Confidence);
        Assert.Contains("light-agent, music-agent", result.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_WithNoAgentsAvailable_ReturnsFallback()
    {
        // Arrange
        SetupAgentRegistry(new List<AgentCard>());

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn on the lights");

        // Act
        var result = await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("general-assistant", result.AgentId);
        Assert.Equal(0, result.Confidence);
        Assert.Contains("No registered agents available", result.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_WithMalformedLLMOutput_RetriesAndFallsBack()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder()
                .WithName("light-agent")
                .WithDescription("Controls lighting devices")
                .Build()
        };

        SetupAgentRegistry(availableAgents);
        
        // Configure chat client to return invalid JSON for all attempts
        A.CallTo(() => _fakeChatClient.GetResponseAsync(
            A<IEnumerable<ChatMessage>>._,
            A<ChatOptions>._,
            A<CancellationToken>._))
            .Returns(new ChatResponse(new[]
            {
                new ChatMessage(ChatRole.Assistant, "{ invalid json }")
            }));

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn on the lights");

        // Act
        var result = await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("general-assistant", result.AgentId);
        Assert.Equal(0, result.Confidence);
        Assert.Contains("failed after 3 attempts", result.Reasoning);
        
        // Verify it retried MaxAttempts times
        A.CallTo(() => _fakeChatClient.GetResponseAsync(
            A<IEnumerable<ChatMessage>>._,
            A<ChatOptions>._,
            A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownAgentSelected_ReturnsFallback()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder()
                .WithName("light-agent")
                .WithDescription("Controls lighting devices")
                .Build()
        };

        var invalidResult = new AgentChoiceResultBuilder()
            .WithAgentId("unknown-agent")
            .WithConfidence(0.9)
            .WithReasoning("Selected non-existent agent")
            .Build();

        SetupAgentRegistry(availableAgents);
        SetupChatClientResponse(invalidResult);

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Do something");

        // Act
        var result = await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("general-assistant", result.AgentId);
        Assert.Equal(0, result.Confidence);
        Assert.Contains("unknown agent 'unknown-agent'", result.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_WithAgentCapabilities_IncludesInPrompt()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder()
                .WithName("light-agent")
                .WithDescription("Controls lighting devices")
                .WithCapabilities(new A2A.AgentCapabilities
                {
                    PushNotifications = true,
                    Streaming = true,
                    StateTransitionHistory = false
                })
                .Build()
        };

        var expectedResult = new AgentChoiceResultBuilder()
            .WithAgentId("light-agent")
            .WithConfidence(0.95)
            .Build();

        SetupAgentRegistry(availableAgents);
        SetupChatClientResponse(expectedResult);

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn on lights");

        // Act
        await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert - Verify chat client was called with capabilities in the prompt
        A.CallTo(() => _fakeChatClient.GetResponseAsync(
            A<IEnumerable<ChatMessage>>.That.Matches(msgs =>
                msgs.Any(m => m.Role == ChatRole.User && 
                             m.Text!.Contains("capabilities: push, streaming"))),
            A<ChatOptions>._,
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_WithSkillExamples_IncludesInPrompt()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder()
                .WithName("light-agent")
                .WithDescription("Controls lighting devices")
                .WithSkill(new A2A.AgentSkill
                {
                    Name = "light-control",
                    Description = "Control lights",
                    Examples = new List<string> { "Turn on kitchen lights", "Dim bedroom to 30%" }
                })
                .Build()
        };

        var expectedResult = new AgentChoiceResultBuilder()
            .WithAgentId("light-agent")
            .WithConfidence(0.95)
            .Build();

        SetupAgentRegistry(availableAgents);
        SetupChatClientResponse(expectedResult);

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn on lights");

        // Act
        await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert - Verify chat client was called with examples in the prompt
        A.CallTo(() => _fakeChatClient.GetResponseAsync(
            A<IEnumerable<ChatMessage>>.That.Matches(msgs =>
                msgs.Any(m => m.Role == ChatRole.User && 
                             m.Text!.Contains("example: Turn on kitchen lights") &&
                             m.Text!.Contains("example: Dim bedroom to 30%"))),
            A<ChatOptions>._,
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_WithAdditionalAgents_NormalizesAndFiltersList()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder().WithName("light-agent").Build(),
            new AgentCardBuilder().WithName("music-agent").Build(),
            new AgentCardBuilder().WithName("climate-agent").Build()
        };

        var resultWithAdditional = new AgentChoiceResultBuilder()
            .WithAgentId("light-agent")
            .WithConfidence(0.85)
            .WithAdditionalAgents(
                "music-agent",
                "light-agent", // Should be removed (primary agent)
                "unknown-agent", // Should be removed (not available)
                "climate-agent"
            )
            .Build();

        SetupAgentRegistry(availableAgents);
        SetupChatClientResponse(resultWithAdditional);

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Create party scene");

        // Act
        var result = await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("light-agent", result.AgentId);
        Assert.NotNull(result.AdditionalAgents);
        Assert.Equal(2, result.AdditionalAgents.Count);
        Assert.Contains("music-agent", result.AdditionalAgents);
        Assert.Contains("climate-agent", result.AdditionalAgents);
        Assert.DoesNotContain("light-agent", result.AdditionalAgents);
        Assert.DoesNotContain("unknown-agent", result.AdditionalAgents);
    }

    [Fact]
    public async Task HandleAsync_WithCancellationToken_PropagatesTokenToLLM()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder().WithName("light-agent").Build()
        };

        SetupAgentRegistry(availableAgents);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn on lights");

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await executor.HandleAsync(userMessage, _fakeContext, cts.Token));
    }

    [Fact]
    public async Task HandleAsync_WithStructuredOutputFormat_ConfiguresChatOptions()
    {
        // Arrange
        var availableAgents = new List<AgentCard>
        {
            new AgentCardBuilder().WithName("light-agent").Build()
        };

        var expectedResult = new AgentChoiceResultBuilder()
            .WithAgentId("light-agent")
            .WithConfidence(0.95)
            .Build();

        SetupAgentRegistry(availableAgents);
        SetupChatClientResponse(expectedResult);

        var executor = CreateExecutor();
        var userMessage = new ChatMessage(ChatRole.User, "Turn on lights");

        // Act
        await executor.HandleAsync(userMessage, _fakeContext, CancellationToken.None);

        // Assert - Verify ChatOptions includes JSON response format
        A.CallTo(() => _fakeChatClient.GetResponseAsync(
            A<IEnumerable<ChatMessage>>._,
            A<ChatOptions>.That.Matches(opts =>
                opts.Temperature == 0.3f &&
                opts.MaxOutputTokens == 500 &&
                opts.ResponseFormat is ChatResponseFormatJson),
            A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void Constructor_WithNullChatClient_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RouterExecutor(null!, _fakeRegistry, _logger, Options.Create(_options)));
        
        Assert.Equal("chatClient", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullRegistry_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RouterExecutor(_fakeChatClient, null!, _logger, Options.Create(_options)));
        
        Assert.Equal("agentRegistry", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithInvalidMaxAttempts_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var invalidOptions = new RouterExecutorOptions
        {
            MaxAttempts = 0
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RouterExecutor(_fakeChatClient, _fakeRegistry, _logger, Options.Create(invalidOptions)));
        
        Assert.Equal("options", exception.ParamName);
        Assert.Contains("MaxAttempts must be at least 1", exception.Message);
    }

    #region Helper Methods

    private RouterExecutor CreateExecutor()
    {
        return new RouterExecutor(_fakeChatClient, _fakeRegistry, _logger, Options.Create(_options));
    }

    private void SetupAgentRegistry(List<AgentCard> agents)
    {
        A.CallTo(() => _fakeRegistry.GetAgentsAsync(A<CancellationToken>._))
            .Returns(agents.ToAsyncEnumerable());
    }

    private void SetupChatClientResponse(AgentChoiceResult result)
    {
        var json = JsonSerializer.Serialize(result, RouterExecutor.JsonSerializerOptions);
        var response = new ChatResponse(new[]
        {
            new ChatMessage(ChatRole.Assistant, json)
        });

        A.CallTo(() => _fakeChatClient.GetResponseAsync(
            A<IEnumerable<ChatMessage>>._,
            A<ChatOptions>._,
            A<CancellationToken>._))
            .Returns(response);
    }

    #endregion
}
