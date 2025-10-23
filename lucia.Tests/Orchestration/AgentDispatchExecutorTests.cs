using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using FakeItEasy;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Tests for AgentDispatchExecutor multi-agent execution logic.
/// </summary>
public class AgentDispatchExecutorTests : TestBase
{
    private readonly IWorkflowContext _fakeContext;
    private readonly ChatMessage _userMessage;

    public AgentDispatchExecutorTests()
    {
        _fakeContext = A.Fake<IWorkflowContext>();
        _userMessage = new ChatMessage(ChatRole.User, "Turn on the bedroom lights and play classical music");
    }

    private AgentDispatchExecutor CreateExecutor(Dictionary<string, AgentExecutorWrapper> wrappers)
    {
        return new AgentDispatchExecutor(wrappers, CreateLogger<AgentDispatchExecutor>());
    }

    private AgentExecutorWrapper CreateFakeWrapper(string agentId, bool success = true, string content = "Success")
    {
        var fakeWrapper = A.Fake<AgentExecutorWrapper>();
        
        var response = new AgentResponse
        {
            AgentId = agentId,
            Content = content,
            Success = success,
            ExecutionTimeMs = 100
        };

        A.CallTo(() => fakeWrapper.HandleAsync(A<ChatMessage>._, A<IWorkflowContext>._, A<CancellationToken>._))
            .Returns(new ValueTask<AgentResponse>(response));

        return fakeWrapper;
    }

    [Fact]
    public async Task HandleAsync_WithSingleAgent_ExecutesOnlyPrimaryAgent()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "User wants to control lights",
            AdditionalAgents = null
        };

        var lightWrapper = CreateFakeWrapper("light-agent", content: "Turned on the bedroom lights");
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("light-agent", result[0].AgentId);
        Assert.True(result[0].Success);
        Assert.Equal("Turned on the bedroom lights", result[0].Content);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleAgents_ExecutesPrimaryAndAdditionalSequentially()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Multiple tasks detected",
            AdditionalAgents = new List<string> { "music-agent" }
        };

        var lightWrapper = CreateFakeWrapper("light-agent", content: "Turned on the bedroom lights");
        var musicWrapper = CreateFakeWrapper("music-agent", content: "Playing classical music");
        
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper },
            { "music-agent", musicWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("light-agent", result[0].AgentId);
        Assert.Equal("music-agent", result[1].AgentId);
        Assert.True(result[0].Success);
        Assert.True(result[1].Success);

        // Verify sequential execution: light-agent called first
        A.CallTo(() => lightWrapper.HandleAsync(A<ChatMessage>._, A<IWorkflowContext>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HandleAsync_WithPartialFailure_ContinuesExecutionAndCollectsAllResponses()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Primary task, secondary may fail",
            AdditionalAgents = new List<string> { "climate-agent" }
        };

        var lightWrapper = CreateFakeWrapper("light-agent", success: false, content: "Lights not available");
        var climateWrapper = CreateFakeWrapper("climate-agent", success: true, content: "Set temperature to 72°F");
        
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper },
            { "climate-agent", climateWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.False(result[0].Success);
        Assert.True(result[1].Success);
    }

    [Fact]
    public async Task HandleAsync_WithAllAgentsFailed_ReturnsAllFailureResponses()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Multiple tasks",
            AdditionalAgents = new List<string> { "music-agent" }
        };

        var lightWrapper = CreateFakeWrapper("light-agent", success: false, content: "Error: device offline");
        var musicWrapper = CreateFakeWrapper("music-agent", success: false, content: "Error: service unavailable");
        
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper },
            { "music-agent", musicWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.False(r.Success));
    }

    [Fact]
    public async Task HandleAsync_WithMissingAdditionalAgent_SkipsAndContinuesWithNextAgent()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Multiple agents",
            AdditionalAgents = new List<string> { "missing-agent", "music-agent" }
        };

        var lightWrapper = CreateFakeWrapper("light-agent", content: "Turned on the lights");
        var musicWrapper = CreateFakeWrapper("music-agent", content: "Playing music");
        
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper },
            { "music-agent", musicWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        // Should execute light-agent and music-agent, skip missing-agent
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.AgentId == "light-agent");
        Assert.Contains(result, r => r.AgentId == "music-agent");
        Assert.DoesNotContain(result, r => r.AgentId == "missing-agent");
    }

    [Fact]
    public async Task HandleAsync_WithEmptyAdditionalAgentsList_ExecutesOnlyPrimaryAgent()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Single agent",
            AdditionalAgents = new List<string>()
        };

        var lightWrapper = CreateFakeWrapper("light-agent", content: "Turned on the lights");
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("light-agent", result[0].AgentId);
    }

    [Fact]
    public async Task HandleAsync_WithNullAdditionalAgentsList_ExecutesOnlyPrimaryAgent()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Single agent",
            AdditionalAgents = null
        };

        var lightWrapper = CreateFakeWrapper("light-agent", content: "Turned on the lights");
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public async Task HandleAsync_CollectsExecutionMetrics()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Multiple metrics",
            AdditionalAgents = new List<string> { "music-agent" }
        };

        var lightResponse = new AgentResponse
        {
            AgentId = "light-agent",
            Content = "Lights on",
            Success = true,
            ExecutionTimeMs = 150
        };
        
        var musicResponse = new AgentResponse
        {
            AgentId = "music-agent",
            Content = "Music playing",
            Success = true,
            ExecutionTimeMs = 200
        };

        var lightWrapper = A.Fake<AgentExecutorWrapper>();
        A.CallTo(() => lightWrapper.HandleAsync(A<ChatMessage>._, A<IWorkflowContext>._, A<CancellationToken>._))
            .Returns(new ValueTask<AgentResponse>(lightResponse));

        var musicWrapper = A.Fake<AgentExecutorWrapper>();
        A.CallTo(() => musicWrapper.HandleAsync(A<ChatMessage>._, A<IWorkflowContext>._, A<CancellationToken>._))
            .Returns(new ValueTask<AgentResponse>(musicResponse));

        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper },
            { "music-agent", musicWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(150, result[0].ExecutionTimeMs);
        Assert.Equal(200, result[1].ExecutionTimeMs);
    }

    [Fact]
    public async Task HandleAsync_PropagatesCancellationToken()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Test cancellation"
        };

        var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<AgentResponse>();
        
        var lightWrapper = A.Fake<AgentExecutorWrapper>();
        A.CallTo(() => lightWrapper.HandleAsync(A<ChatMessage>._, A<IWorkflowContext>._, A<CancellationToken>._))
            .Returns(new ValueTask<AgentResponse>(tcs.Task));

        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var task = executor.HandleAsync(agentChoice, _fakeContext, cts.Token);
        cts.Cancel();
        
        // Assert - task should be canceled
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task HandleAsync_WithThreeAgents_ExecutesAllInSequence()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Three agents",
            AdditionalAgents = new List<string> { "music-agent", "climate-agent" }
        };

        var lightWrapper = CreateFakeWrapper("light-agent", content: "Lights on");
        var musicWrapper = CreateFakeWrapper("music-agent", content: "Music on");
        var climateWrapper = CreateFakeWrapper("climate-agent", content: "Temperature set");
        
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper },
            { "music-agent", musicWrapper },
            { "climate-agent", climateWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("light-agent", result[0].AgentId);
        Assert.Equal("music-agent", result[1].AgentId);
        Assert.Equal("climate-agent", result[2].AgentId);
        Assert.All(result, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task HandleAsync_WithMixedSuccessAndFailure_PreservesBothResponses()
    {
        // Arrange
        var agentChoice = new AgentChoiceResult
        {
            AgentId = "light-agent",
            Confidence = 0.95,
            Reasoning = "Mixed results",
            AdditionalAgents = new List<string> { "music-agent", "climate-agent" }
        };

        var lightWrapper = CreateFakeWrapper("light-agent", success: true, content: "Lights on");
        var musicWrapper = CreateFakeWrapper("music-agent", success: false, content: "Music service down");
        var climateWrapper = CreateFakeWrapper("climate-agent", success: true, content: "Temperature set");
        
        var wrappers = new Dictionary<string, AgentExecutorWrapper>
        {
            { "light-agent", lightWrapper },
            { "music-agent", musicWrapper },
            { "climate-agent", climateWrapper }
        };

        var executor = CreateExecutor(wrappers);
        executor.SetUserMessage(_userMessage);

        // Act
        var result = await executor.HandleAsync(agentChoice, _fakeContext, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.True(result[0].Success);  // light
        Assert.False(result[1].Success); // music
        Assert.True(result[2].Success);  // climate
    }

    [Fact]
    public void Constructor_WithNullWrappers_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new AgentDispatchExecutor(null!, CreateLogger<AgentDispatchExecutor>()));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new AgentDispatchExecutor(new Dictionary<string, AgentExecutorWrapper>(), null!));
    }
}
