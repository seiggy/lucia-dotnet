using FakeItEasy;

using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Provider-free unit tests for <see cref="ResultAggregatorExecutor"/> response composition.
/// Covers success, failure, mixed, empty, priority-ordering, NeedsInput propagation, and
/// agent name formatting — all without a live LLM provider.
/// </summary>
public sealed class ResultAggregatorExecutorTests
{
    // ─── Helpers ─────────────────────────────────────────────────────

    private static IWorkflowContext CreateContext()
    {
        var context = A.Fake<IWorkflowContext>();
        A.CallTo(() => context.AddEventAsync(A<WorkflowEvent>._, A<CancellationToken>._))
            .Returns(new ValueTask());
        return context;
    }

    private static ResultAggregatorExecutor CreateAggregator(
        ResultAggregatorOptions? options = null)
    {
        return new ResultAggregatorExecutor(
            NullLogger<ResultAggregatorExecutor>.Instance,
            Options.Create(options ?? new ResultAggregatorOptions()));
    }

    private static OrchestratorAgentResponse Success(string agentId, string content = "Done.") =>
        new()
        {
            AgentId = agentId,
            Content = content,
            Success = true,
            ExecutionTimeMs = 1
        };

    private static OrchestratorAgentResponse Failure(string agentId, string error = "Service unavailable") =>
        new()
        {
            AgentId = agentId,
            Content = string.Empty,
            Success = false,
            ErrorMessage = error,
            ExecutionTimeMs = 1
        };

    // ─── Single success ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SingleSuccess_ReturnsAgentContent()
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            Success("light-agent", "The kitchen lights are now on.")
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.Equal("The kitchen lights are now on.", result.Text);
        Assert.False(result.NeedsInput);
    }

    // ─── Success with empty content → default template ────────────────

    [Fact]
    public async Task HandleAsync_SuccessWithEmptyContent_UsesDefaultSuccessTemplate()
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            Success("light-agent", string.Empty)
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.Contains("Light Agent", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("completed", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Single failure ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SingleFailure_ReturnsFailureMessage()
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            Failure("music-agent", "no speaker available")
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.Contains("couldn't complete", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Music Agent", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no speaker available", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Multiple failures ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_MultipleFailures_ReturnsMultiFailureFormat()
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            Failure("music-agent", "no speaker"),
            Failure("light-agent", "device offline")
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.Contains("issues with", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Mixed success + failure ──────────────────────────────────────

    [Fact]
    public async Task HandleAsync_MixedSuccessAndFailure_IncludesBothParts()
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            Success("light-agent", "Lights are on."),
            Failure("music-agent", "no speaker available")
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.Contains("Lights are on", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("couldn't complete", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Music Agent", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Empty response list ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_EmptyResponses_ReturnsFallbackMessage()
    {
        var options = new ResultAggregatorOptions
        {
            DefaultFallbackMessage = "Still working on that."
        };
        var aggregator = CreateAggregator(options);

        var result = await aggregator.HandleAsync(
            new List<OrchestratorAgentResponse>(),
            CreateContext(),
            CancellationToken.None);

        Assert.Equal("Still working on that.", result.Text);
    }

    // ─── NeedsInput propagation ───────────────────────────────────────

    [Fact]
    public async Task HandleAsync_AgentNeedsInput_SetsNeedsInputOnResult()
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            new()
            {
                AgentId = "light-agent",
                Content = "Which lights should I turn on?",
                Success = true,
                NeedsInput = true,
                ExecutionTimeMs = 1
            }
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.True(result.NeedsInput);
    }

    // ─── Multiple successes joined ────────────────────────────────────

    [Fact]
    public async Task HandleAsync_MultipleSuccesses_JoinsAllContentInResult()
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            Success("light-agent", "Lights dimmed."),
            Success("music-agent", "Playing jazz.")
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.Contains("Lights dimmed", result.Text);
        Assert.Contains("Playing jazz", result.Text);
    }

    // ─── Priority ordering ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_AgentPriority_PlacesLightAgentBeforeMusicAgent()
    {
        // Default priority list: light-agent (index 0), music-agent (index 1)
        var aggregator = CreateAggregator();

        // Provide responses in reverse priority order to confirm ordering is applied
        var responses = new List<OrchestratorAgentResponse>
        {
            Success("music-agent", "Playing jazz."),
            Success("light-agent", "Lights dimmed.")
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        var lightIdx = result.Text.IndexOf("Lights dimmed", StringComparison.OrdinalIgnoreCase);
        var musicIdx = result.Text.IndexOf("Playing jazz", StringComparison.OrdinalIgnoreCase);
        Assert.True(lightIdx < musicIdx,
            "light-agent response should appear before music-agent response per default priority");
    }

    // ─── Agent name formatting ────────────────────────────────────────

    [Theory]
    [InlineData("light-agent", "Light Agent")]
    [InlineData("music-agent", "Music Agent")]
    [InlineData("climate-agent", "Climate Agent")]
    [InlineData("general-assistant", "General Assistant")]
    public async Task HandleAsync_AgentNameFormatting_ProducesTitleCaseInFailureMessage(
        string agentId, string expectedName)
    {
        var aggregator = CreateAggregator();
        var responses = new List<OrchestratorAgentResponse>
        {
            Failure(agentId, "some error")
        };

        var result = await aggregator.HandleAsync(responses, CreateContext(), CancellationToken.None);

        Assert.Contains(expectedName, result.Text, StringComparison.OrdinalIgnoreCase);
    }
}
