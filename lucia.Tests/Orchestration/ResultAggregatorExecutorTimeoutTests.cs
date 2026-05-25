using FakeItEasy;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

public sealed class ResultAggregatorExecutorTimeoutTests
{
    [Fact]
    public async Task HandleAsync_WhenPipelineCancellationArrivesDuringAggregation_ReturnsGracefulAgentFailure()
    {
        var context = A.Fake<IWorkflowContext>();
        A.CallTo(() => context.AddEventAsync(A<WorkflowEvent>._, A<CancellationToken>._))
            .Invokes(call => call.GetArgument<CancellationToken>(1).ThrowIfCancellationRequested())
            .Returns(new ValueTask());

        var aggregator = new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(new ResultAggregatorOptions()));
        var responses = new List<OrchestratorAgentResponse>
        {
            new()
            {
                AgentId = "music-agent",
                Content = string.Empty,
                Success = false,
                ErrorMessage = "the request was interrupted before the agent finished.",
                ExecutionTimeMs = 9162
            }
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await aggregator.HandleAsync(responses, context, cts.Token);

        Assert.Equal("However, I couldn't complete Music Agent: the request was interrupted before the agent finished.", result.Text);
        Assert.False(result.NeedsInput);
    }
}
