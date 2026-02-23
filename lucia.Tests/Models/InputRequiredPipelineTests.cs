using FakeItEasy;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Models;

/// <summary>
/// Tests for the InputRequired/NeedsInput signal flowing through the pipeline.
/// </summary>
public sealed class InputRequiredPipelineTests : TestBase
{
    [Fact]
    public void OrchestratorAgentResponse_NeedsInput_DefaultsFalse()
    {
        var response = new AgentResponseBuilder().Build();
        Assert.False(response.NeedsInput);
    }

    [Fact]
    public void OrchestratorAgentResponse_NeedsInput_CanBeSetTrue()
    {
        var response = new AgentResponseBuilder()
            .WithNeedsInput()
            .WithContent("Which room would you like?")
            .Build();

        Assert.True(response.NeedsInput);
        Assert.Equal("Which room would you like?", response.Content);
    }

    [Fact]
    public void OrchestratorResult_NeedsInput_DefaultsFalse()
    {
        var result = new OrchestratorResult { Text = "Done" };
        Assert.False(result.NeedsInput);
    }

    [Fact]
    public void OrchestratorResult_NeedsInput_CanBeSetTrue()
    {
        var result = new OrchestratorResult { Text = "Which light?", NeedsInput = true };
        Assert.True(result.NeedsInput);
    }

    [Fact]
    public void AggregationResult_NeedsInput_DefaultsFalse()
    {
        var result = new AggregationResult("All good", ["agent-1"], [], 100);
        Assert.False(result.NeedsInput);
    }

    [Fact]
    public void AggregationResult_NeedsInput_CanBeSetTrue()
    {
        var result = new AggregationResult("Need clarification", ["agent-1"], [], 100, NeedsInput: true);
        Assert.True(result.NeedsInput);
    }

    [Fact]
    public async Task Aggregator_PropagatesNeedsInput_WhenAnyResponseNeedsIt()
    {
        // Arrange
        var options = Options.Create(new ResultAggregatorOptions());
        var logger = CreateLogger<ResultAggregatorExecutor>();
        var aggregator = new ResultAggregatorExecutor(logger, options);

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Lights are on")
                .Build(),
            new AgentResponseBuilder()
                .WithAgentId("music-agent")
                .WithNeedsInput()
                .WithContent("Which playlist would you like?")
                .Build(),
        };

        var context = A.Fake<IWorkflowContext>();

        // Act
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert
        Assert.True(result.NeedsInput);
        Assert.Contains("Which playlist would you like?", result.Text);
    }

    [Fact]
    public async Task Aggregator_NeedsInputFalse_WhenNoResponseNeedsIt()
    {
        // Arrange
        var options = Options.Create(new ResultAggregatorOptions());
        var logger = CreateLogger<ResultAggregatorExecutor>();
        var aggregator = new ResultAggregatorExecutor(logger, options);

        var responses = new List<OrchestratorAgentResponse>
        {
            new AgentResponseBuilder()
                .WithAgentId("light-agent")
                .WithContent("Lights are on")
                .Build(),
            new AgentResponseBuilder()
                .WithAgentId("music-agent")
                .WithContent("Playing jazz")
                .Build(),
        };

        var context = A.Fake<IWorkflowContext>();

        // Act
        var result = await aggregator.HandleAsync(responses, context, CancellationToken.None);

        // Assert
        Assert.False(result.NeedsInput);
    }
}
