using System.Linq;
using lucia.Agents.Orchestration;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

public class ResultAggregatorExecutorTests
{
    private static ResultAggregatorExecutor CreateExecutor(ResultAggregatorOptions? options = null)
    {
        var opts = Options.Create(options ?? new ResultAggregatorOptions());
        return new ResultAggregatorExecutor(NullLogger<ResultAggregatorExecutor>.Instance, opts);
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
}
