#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Evaluation tests for the full Lucia orchestrator pipeline. Exercises the real
/// <see cref="lucia.Agents.Orchestration.LuciaOrchestrator.ProcessRequestAsync"/> workflow —
/// Router → AgentDispatch → ResultAggregator — with real agents backed by eval models.
/// Intermediate pipeline events are captured via <see cref="OrchestratorEvalObserver"/>.
/// </summary>
[Trait("Category", "Eval")]
[Trait("Agent", "Orchestrator")]
public sealed class OrchestratorEvalTests : AgentEvalTestBase
{
    public OrchestratorEvalTests(EvalTestFixture fixture) : base(fixture) { }

    // ─── Intent Resolution: single agent routing ───────────────────────

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task RouteToLightAgent_LightRequest_ReturnsLightAgentId(string modelId)
    {
        var observer = new OrchestratorEvalObserver();
        var orchestrator = Fixture.CreateLuciaOrchestrator(modelId, observer);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: true,
            includeToolEvaluators: false,
            new A2AToolCallEvaluator());

        var (_, result) = await RunOrchestratorAndEvaluateAsync(
            modelId,
            orchestrator,
            observer,
            "Turn on the kitchen lights",
            reportingConfig,
            "Orchestrator.RouteToLightAgent_LightRequest",
            expectedAgentIds: ["light"]);

        Assert.NotNull(observer.RoutingDecision);
        Assert.Contains("light", observer.RoutingDecision.AgentId, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(observer.AgentResponses);
        Assert.NotNull(observer.AggregatedResponse);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task RouteToMusicAgent_MusicRequest_ReturnsMusicAgentId(string modelId)
    {
        var observer = new OrchestratorEvalObserver();
        var orchestrator = Fixture.CreateLuciaOrchestrator(modelId, observer);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: true,
            includeToolEvaluators: false,
            new A2AToolCallEvaluator());

        var (_, result) = await RunOrchestratorAndEvaluateAsync(
            modelId,
            orchestrator,
            observer,
            "Play some jazz music on the kitchen speaker",
            reportingConfig,
            "Orchestrator.RouteToMusicAgent_MusicRequest",
            expectedAgentIds: ["music"]);

        Assert.NotNull(observer.RoutingDecision);
        Assert.Contains("music", observer.RoutingDecision.AgentId, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(observer.AgentResponses);
        Assert.NotNull(observer.AggregatedResponse);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task RouteToLightAgent_DimmingRequest_ReturnsLightAgentId(string modelId)
    {
        var observer = new OrchestratorEvalObserver();
        var orchestrator = Fixture.CreateLuciaOrchestrator(modelId, observer);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: true,
            includeToolEvaluators: false,
            new A2AToolCallEvaluator());

        var (_, result) = await RunOrchestratorAndEvaluateAsync(
            modelId,
            orchestrator,
            observer,
            "Dim the bedroom lamp to 30%",
            reportingConfig,
            "Orchestrator.RouteToLightAgent_DimmingRequest",
            expectedAgentIds: ["light"]);

        Assert.NotNull(observer.RoutingDecision);
        Assert.Contains("light", observer.RoutingDecision.AgentId, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(observer.AgentResponses);
        Assert.NotNull(observer.AggregatedResponse);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Intent Resolution: multi-agent routing ────────────────────────

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task RouteMultiAgent_LightAndMusic_RoutesToBoth(string modelId)
    {
        var observer = new OrchestratorEvalObserver();
        var orchestrator = Fixture.CreateLuciaOrchestrator(modelId, observer);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: true,
            includeToolEvaluators: false,
            new A2AToolCallEvaluator());

        var (_, result) = await RunOrchestratorAndEvaluateAsync(
            modelId,
            orchestrator,
            observer,
            "Dim the living room lights and play some soft music",
            reportingConfig,
            "Orchestrator.RouteMultiAgent_LightAndMusic",
            expectedAgentIds: ["light", "music"]);

        Assert.NotNull(observer.RoutingDecision);

        // The orchestrator should route to one agent primarily and reference the other
        // in AdditionalAgents, or the primary could be either.
        var allAgents = new List<string> { observer.RoutingDecision.AgentId };
        if (observer.RoutingDecision.AdditionalAgents is not null)
        {
            allAgents.AddRange(observer.RoutingDecision.AdditionalAgents);
        }

        var allAgentsText = string.Join(", ", allAgents);
        Assert.True(
            allAgents.Any(a => a.Contains("light", StringComparison.OrdinalIgnoreCase)) &&
            allAgents.Any(a => a.Contains("music", StringComparison.OrdinalIgnoreCase)),
            $"Expected both light and music agents in routing result. Got: {allAgentsText}");

        // Multi-agent dispatch should produce responses from multiple agents
        Assert.True(observer.AgentResponses.Count >= 2,
            $"Expected at least 2 agent responses for multi-agent dispatch, got {observer.AgentResponses.Count}");
        Assert.NotNull(observer.AggregatedResponse);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Task Adherence ────────────────────────────────────────────────

    [Trait("Evaluator", "TaskAdherence")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task Confidence_ClearRequest_HighConfidence(string modelId)
    {
        var observer = new OrchestratorEvalObserver();
        var orchestrator = Fixture.CreateLuciaOrchestrator(modelId, observer);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: true,
            includeToolEvaluators: false,
            new A2AToolCallEvaluator());

        var (_, result) = await RunOrchestratorAndEvaluateAsync(
            modelId,
            orchestrator,
            observer,
            "Turn on the kitchen lights",
            reportingConfig,
            "Orchestrator.Confidence_ClearRequest",
            expectedAgentIds: ["light"]);

        Assert.NotNull(observer.RoutingDecision);
        Assert.True(observer.RoutingDecision.Confidence >= 0.7,
            $"Expected confidence >= 0.7 for clear request, got {observer.RoutingDecision.Confidence}");

        Assert.NotNull(observer.AggregatedResponse);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "TaskAdherence")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task Confidence_AmbiguousRequest_LowerConfidence(string modelId)
    {
        var observer = new OrchestratorEvalObserver();
        var orchestrator = Fixture.CreateLuciaOrchestrator(modelId, observer);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: true,
            includeToolEvaluators: false,
            new A2AToolCallEvaluator());

        var (_, result) = await RunOrchestratorAndEvaluateAsync(
            modelId,
            orchestrator,
            observer,
            "Do something in the bedroom",
            reportingConfig,
            "Orchestrator.Confidence_AmbiguousRequest");

        Assert.NotNull(observer.RoutingDecision);
        // Ambiguous requests should have lower confidence
        Assert.True(observer.RoutingDecision.Confidence < 0.9,
            $"Expected confidence < 0.9 for ambiguous request, got {observer.RoutingDecision.Confidence}");

        Assert.NotNull(observer.AggregatedResponse);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(ModelIds))]
    public async Task FallbackAgent_GeneralKnowledge_RoutesToFallback(string modelId)
    {
        var observer = new OrchestratorEvalObserver();
        var orchestrator = Fixture.CreateLuciaOrchestrator(modelId, observer);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: true,
            includeToolEvaluators: false,
            new A2AToolCallEvaluator());

        var (_, result) = await RunOrchestratorAndEvaluateAsync(
            modelId,
            orchestrator,
            observer,
            "What is the capital of France?",
            reportingConfig,
            "Orchestrator.FallbackAgent_GeneralKnowledge",
            expectedAgentIds: ["general"]);

        Assert.NotNull(observer.RoutingDecision);
        // General knowledge requests should route to the fallback / general-assistant
        Assert.Contains("general", observer.RoutingDecision.AgentId, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(observer.AgentResponses);
        Assert.NotNull(observer.AggregatedResponse);
        AssertNoUnacceptableMetrics(result);
    }
}
