using System.Linq;
using lucia.Agents.Orchestration;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Tests for ResultAggregatorExecutor response aggregation logic.
/// </summary>
public class ResultAggregatorExecutorTests : TestBase
{
    private readonly ResultAggregatorOptions _options;

    public ResultAggregatorExecutorTests()
    {
        _options = new ResultAggregatorOptions
        {
            AgentPriority = ["light-agent", "music-agent", "climate-agent"],
            DefaultSuccessTemplate = "Completed: {0}",
            DefaultFallbackMessage = "Unable to process request",
            DefaultFailureMessage = "Operation failed"
        };
    }

    private ResultAggregatorExecutor CreateExecutor(ResultAggregatorOptions? options = null)
    {
        var opts = Options.Create(options ?? _options);
        return new ResultAggregatorExecutor(CreateLogger<ResultAggregatorExecutor>(), opts);
    }

    [Fact]
    public async Task HandleAsync_ReturnsSingleAgentContent()
    {
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();
        var response = new AgentResponse
        {
            AgentId = "light-agent",
            Content = "I've turned on the kitchen lights.",
            Success = true,
            ExecutionTimeMs = 120
        };

        var result = await executor.HandleAsync(response, context);

        Assert.Equal(response.Content, result);

        var stored = (ResultAggregationState?)context.State[(ResultAggregatorExecutor.StateScope, ResultAggregatorExecutor.StateKey)];
        Assert.NotNull(stored);
        Assert.True(stored!.Responses.ContainsKey("light-agent"));

        var completed = context.Events.OfType<ExecutorCompletedEvent>().Last();
        var telemetry = Assert.IsType<AggregationResult>(completed.Data);
        Assert.Equal(result, telemetry.Message);
        Assert.Equal(new[] { "light-agent" }, telemetry.SuccessfulAgents);
        Assert.Empty(telemetry.FailedAgents);
        Assert.Equal(response.ExecutionTimeMs, telemetry.TotalExecutionTimeMs);
    }

    [Fact]
    public async Task HandleAsync_AggregatesMultipleResponsesInPriorityOrder()
    {
        var options = new ResultAggregatorOptions
        {
            AgentPriority = ["light-agent", "music-agent", "climate-agent"]
        };

        var executor = CreateExecutor(options);
        var context = new RecordingWorkflowContext();

        await executor.HandleAsync(new AgentResponse
        {
            AgentId = "music-agent",
            Content = "Started playing relaxing jazz in the living room.",
            Success = true,
            ExecutionTimeMs = 210
        }, context);

        var combined = await executor.HandleAsync(new AgentResponse
        {
            AgentId = "light-agent",
            Content = "Dimmed the living room lights to 30%.",
            Success = true,
            ExecutionTimeMs = 95
        }, context);

        var lightIndex = combined.IndexOf("Dimmed the living room lights", StringComparison.Ordinal);
        var musicIndex = combined.IndexOf("Started playing relaxing jazz", StringComparison.Ordinal);

        Assert.InRange(lightIndex, 0, musicIndex - 1);

        var completed = context.Events.OfType<ExecutorCompletedEvent>().Last();
        var telemetry = Assert.IsType<AggregationResult>(completed.Data);
        Assert.Equal(["light-agent", "music-agent"], telemetry.SuccessfulAgents);
        Assert.Equal(95 + 210, telemetry.TotalExecutionTimeMs);
    }

    [Fact]
    public async Task HandleAsync_AppendsFailureNotice()
    {
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();

        await executor.HandleAsync(new AgentResponse
        {
            AgentId = "light-agent",
            Content = "I've turned on the hallway lights.",
            Success = true,
            ExecutionTimeMs = 80
        }, context);

        var combined = await executor.HandleAsync(new AgentResponse
        {
            AgentId = "music-agent",
            Content = string.Empty,
            Success = false,
            ErrorMessage = "Speaker offline",
            ExecutionTimeMs = 40
        }, context);

        Assert.Contains("However", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Speaker offline", combined, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(context.Events, evt => evt is ExecutorFailedEvent);

        var telemetry = Assert.IsType<AggregationResult>(context.Events.OfType<ExecutorCompletedEvent>().Last().Data);
        Assert.Single(telemetry.FailedAgents);
        Assert.Equal("music-agent", telemetry.FailedAgents[0].AgentId);
        Assert.Equal("Speaker offline", telemetry.FailedAgents[0].Error);
    }

    [Fact]
    public async Task HandleAsync_WithAllAgentsFailed_ReturnsFallbackMessage()
    {
        // Arrange
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();

        await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithSuccess(false)
            .WithErrorMessage("Light controller offline")
            .WithExecutionTime(50)
            .Build(), context);

        // Act
        var result = await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("music-agent")
            .WithSuccess(false)
            .WithErrorMessage("Music service unavailable")
            .WithExecutionTime(30)
            .Build(), context);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("issues", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Light controller offline", result);
        Assert.Contains("Music service unavailable", result);

        var telemetry = Assert.IsType<AggregationResult>(context.Events.OfType<ExecutorCompletedEvent>().Last().Data);
        Assert.Empty(telemetry.SuccessfulAgents);
        Assert.Equal(2, telemetry.FailedAgents.Count);
        Assert.Equal(80L, telemetry.TotalExecutionTimeMs);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyContent_HandlesGracefully()
    {
        // Arrange
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();

        // Act
        var result = await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithContent(string.Empty)
            .WithSuccess(true)
            .Build(), context);

        // Assert
        Assert.NotNull(result);
        // Should still return some message even with empty content
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleFailures_ListsAllErrors()
    {
        // Arrange
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();

        await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithSuccess(false)
            .WithErrorMessage("Network timeout")
            .Build(), context);

        await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("music-agent")
            .WithSuccess(false)
            .WithErrorMessage("Device not found")
            .Build(), context);

        // Act
        var result = await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("climate-agent")
            .WithSuccess(false)
            .WithErrorMessage("Permission denied")
            .Build(), context);

        // Assert
        Assert.Contains("Network timeout", result);
        Assert.Contains("Device not found", result);
        Assert.Contains("Permission denied", result);

        var telemetry = Assert.IsType<AggregationResult>(context.Events.OfType<ExecutorCompletedEvent>().Last().Data);
        Assert.Equal(3, telemetry.FailedAgents.Count);
    }

    [Fact]
    public async Task HandleAsync_PreservesStateAcrossMultipleCalls()
    {
        // Arrange
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();

        // Act - First call
        var firstResult = await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithContent("Lights on")
            .WithSuccess(true)
            .Build(), context);

        // Verify state was stored
        var state1 = (ResultAggregationState?)context.State[(ResultAggregatorExecutor.StateScope, ResultAggregatorExecutor.StateKey)];
        Assert.NotNull(state1);
        Assert.Single(state1!.Responses);

        // Act - Second call
        var secondResult = await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("music-agent")
            .WithContent("Music playing")
            .WithSuccess(true)
            .Build(), context);

        // Assert - State accumulated
        var state2 = (ResultAggregationState?)context.State[(ResultAggregatorExecutor.StateScope, ResultAggregatorExecutor.StateKey)];
        Assert.NotNull(state2);
        Assert.Equal(2, state2!.Responses.Count);
        Assert.Contains("light-agent", state2.Responses.Keys);
        Assert.Contains("music-agent", state2.Responses.Keys);
    }

    [Fact]
    public async Task HandleAsync_EmitsTelemetryEvents()
    {
        // Arrange
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();

        // Act
        await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithContent("Success")
            .WithSuccess(true)
            .WithExecutionTime(100)
            .Build(), context);

        // Assert
        var invokedEvents = context.Events.OfType<ExecutorInvokedEvent>().ToList();
        var completedEvents = context.Events.OfType<ExecutorCompletedEvent>().ToList();

        Assert.Single(invokedEvents);
        Assert.Single(completedEvents);
        Assert.Equal(ResultAggregatorExecutor.ExecutorId, invokedEvents[0].ExecutorId);
        Assert.Equal(ResultAggregatorExecutor.ExecutorId, completedEvents[0].ExecutorId);

        var telemetry = Assert.IsType<AggregationResult>(completedEvents[0].Data);
        Assert.NotNull(telemetry.Message);
        Assert.Single(telemetry.SuccessfulAgents);
        Assert.Empty(telemetry.FailedAgents);
        Assert.Equal(100L, telemetry.TotalExecutionTimeMs);
    }

    [Fact]
    public async Task HandleAsync_WithNegativeExecutionTime_NormalizesToZero()
    {
        // Arrange
        var executor = CreateExecutor();
        var context = new RecordingWorkflowContext();

        // Act
        await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithContent("Success")
            .WithSuccess(true)
            .WithExecutionTime(-50) // Invalid negative time
            .Build(), context);

        // Assert
        var telemetry = Assert.IsType<AggregationResult>(context.Events.OfType<ExecutorCompletedEvent>().Last().Data);
        Assert.Equal(0L, telemetry.TotalExecutionTimeMs); // Should be normalized to 0
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResultAggregatorExecutor(null!, Options.Create(_options)));
        
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ResultAggregatorExecutor(CreateLogger<ResultAggregatorExecutor>(), null!));
        
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public async Task HandleAsync_WithCustomAgentPriority_OrdersResponsesCorrectly()
    {
        // Arrange
        var customOptions = new ResultAggregatorOptions
        {
            AgentPriority = ["climate-agent", "light-agent", "music-agent"]
        };
        var executor = CreateExecutor(customOptions);
        var context = new RecordingWorkflowContext();

        // Add agents in wrong order
        await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("music-agent")
            .WithContent("Playing music")
            .WithSuccess(true)
            .Build(), context);

        await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithContent("Lights adjusted")
            .WithSuccess(true)
            .Build(), context);

        // Act
        var result = await executor.HandleAsync(new AgentResponseBuilder()
            .WithAgentId("climate-agent")
            .WithContent("Temperature set")
            .WithSuccess(true)
            .Build(), context);

        // Assert - Climate should come first due to priority
        var climateIndex = result.IndexOf("Temperature set", StringComparison.Ordinal);
        var lightIndex = result.IndexOf("Lights adjusted", StringComparison.Ordinal);
        var musicIndex = result.IndexOf("Playing music", StringComparison.Ordinal);

        Assert.InRange(climateIndex, 0, lightIndex - 1);
        Assert.InRange(lightIndex, climateIndex + 1, musicIndex - 1);
    }
}
