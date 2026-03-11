using System.Diagnostics;
using lucia.Agents.Orchestration;

namespace lucia.Evals;

/// <summary>
/// Orchestrator evaluation scenarios — tests the full
/// Router → AgentDispatch → ResultAggregator pipeline
/// for intent routing, multi-agent dispatch, confidence calibration,
/// and fallback behavior.
/// </summary>
public sealed class OrchestratorScenarios
{
    public static EvalScenarioGroup Create(EvalTestFixture fixture)
    {
        var scenarios = new List<EvalScenario>();

        foreach (var (modelId, embeddingModelId) in GetModelPairs(fixture))
        {
            // ── Intent Resolution: single agent routing ───────────────

            scenarios.Add(CreateOrchestratorScenario(
                fixture, modelId, embeddingModelId,
                name: "RouteToLightAgent",
                prompt: "Turn on the kitchen lights",
                tags: ["orchestrator", "routing", "light", "intent-resolution"],
                validate: (observer, resultText) =>
                {
                    if (observer.RoutingDecision is null)
                        return "No routing decision was made";
                    if (!observer.RoutingDecision.AgentId.Contains("light", StringComparison.OrdinalIgnoreCase))
                        return $"Expected routing to light agent, got: {observer.RoutingDecision.AgentId}";
                    if (observer.AgentResponses.Count == 0)
                        return "No agent responses were collected";
                    if (observer.AggregatedResponse is null)
                        return "No aggregated response was produced";
                    if (string.IsNullOrWhiteSpace(resultText))
                        return "Expected non-empty orchestrator response";
                    return null;
                }));

            scenarios.Add(CreateOrchestratorScenario(
                fixture, modelId, embeddingModelId,
                name: "RouteToMusicAgent",
                prompt: "Play some jazz music on the kitchen speaker",
                tags: ["orchestrator", "routing", "music", "intent-resolution"],
                validate: (observer, resultText) =>
                {
                    if (observer.RoutingDecision is null)
                        return "No routing decision was made";
                    if (!observer.RoutingDecision.AgentId.Contains("music", StringComparison.OrdinalIgnoreCase))
                        return $"Expected routing to music agent, got: {observer.RoutingDecision.AgentId}";
                    if (observer.AgentResponses.Count == 0)
                        return "No agent responses were collected";
                    if (observer.AggregatedResponse is null)
                        return "No aggregated response was produced";
                    if (string.IsNullOrWhiteSpace(resultText))
                        return "Expected non-empty orchestrator response";
                    return null;
                }));

            scenarios.Add(CreateOrchestratorScenario(
                fixture, modelId, embeddingModelId,
                name: "RouteToLightAgent_Dimming",
                prompt: "Dim the bedroom lamp to 30%",
                tags: ["orchestrator", "routing", "light", "dim", "intent-resolution"],
                validate: (observer, resultText) =>
                {
                    if (observer.RoutingDecision is null)
                        return "No routing decision was made";
                    if (!observer.RoutingDecision.AgentId.Contains("light", StringComparison.OrdinalIgnoreCase))
                        return $"Expected routing to light agent, got: {observer.RoutingDecision.AgentId}";
                    if (observer.AgentResponses.Count == 0)
                        return "No agent responses were collected";
                    if (observer.AggregatedResponse is null)
                        return "No aggregated response was produced";
                    if (string.IsNullOrWhiteSpace(resultText))
                        return "Expected non-empty orchestrator response";
                    return null;
                }));

            // ── Intent Resolution: multi-agent routing ────────────────

            scenarios.Add(CreateOrchestratorScenario(
                fixture, modelId, embeddingModelId,
                name: "RouteMultiAgent",
                prompt: "Dim the living room lights and play some soft music",
                tags: ["orchestrator", "routing", "multi-agent", "intent-resolution"],
                validate: (observer, resultText) =>
                {
                    if (observer.RoutingDecision is null)
                        return "No routing decision was made";

                    var allAgents = new List<string> { observer.RoutingDecision.AgentId };
                    if (observer.RoutingDecision.AdditionalAgents is not null)
                        allAgents.AddRange(observer.RoutingDecision.AdditionalAgents);

                    var hasLight = allAgents.Any(a => a.Contains("light", StringComparison.OrdinalIgnoreCase));
                    var hasMusic = allAgents.Any(a => a.Contains("music", StringComparison.OrdinalIgnoreCase));

                    if (!hasLight || !hasMusic)
                        return $"Expected both light and music agents, got: {string.Join(", ", allAgents)}";

                    if (observer.AgentResponses.Count < 2)
                        return $"Expected at least 2 agent responses, got {observer.AgentResponses.Count}";
                    if (observer.AggregatedResponse is null)
                        return "No aggregated response was produced";
                    if (string.IsNullOrWhiteSpace(resultText))
                        return "Expected non-empty orchestrator response";
                    return null;
                }));

            // ── Task Adherence: confidence calibration ─────────────────

            scenarios.Add(CreateOrchestratorScenario(
                fixture, modelId, embeddingModelId,
                name: "Confidence_Clear",
                prompt: "Turn on the kitchen lights",
                tags: ["orchestrator", "confidence", "clear", "task-adherence"],
                validate: (observer, resultText) =>
                {
                    if (observer.RoutingDecision is null)
                        return "No routing decision was made";
                    if (observer.RoutingDecision.Confidence < 0.7)
                        return $"Expected confidence >= 0.7 for clear request, got {observer.RoutingDecision.Confidence}";
                    if (observer.AggregatedResponse is null)
                        return "No aggregated response was produced";
                    if (string.IsNullOrWhiteSpace(resultText))
                        return "Expected non-empty orchestrator response";
                    return null;
                }));

            scenarios.Add(CreateOrchestratorScenario(
                fixture, modelId, embeddingModelId,
                name: "Confidence_Ambiguous",
                prompt: "Do something in the bedroom",
                tags: ["orchestrator", "confidence", "ambiguous", "task-adherence"],
                validate: (observer, resultText) =>
                {
                    if (observer.RoutingDecision is null)
                        return "No routing decision was made";
                    if (observer.RoutingDecision.Confidence >= 0.9)
                        return $"Expected confidence < 0.9 for ambiguous request, got {observer.RoutingDecision.Confidence}";
                    if (observer.AggregatedResponse is null)
                        return "No aggregated response was produced";
                    if (string.IsNullOrWhiteSpace(resultText))
                        return "Expected non-empty orchestrator response";
                    return null;
                }));

            // ── Intent Resolution: fallback ───────────────────────────

            scenarios.Add(CreateOrchestratorScenario(
                fixture, modelId, embeddingModelId,
                name: "FallbackAgent",
                prompt: "What is the capital of France?",
                tags: ["orchestrator", "routing", "fallback", "intent-resolution"],
                validate: (observer, resultText) =>
                {
                    if (observer.RoutingDecision is null)
                        return "No routing decision was made";
                    if (!observer.RoutingDecision.AgentId.Contains("general", StringComparison.OrdinalIgnoreCase))
                        return $"Expected routing to general/fallback agent, got: {observer.RoutingDecision.AgentId}";
                    if (observer.AgentResponses.Count == 0)
                        return "No agent responses were collected";
                    if (observer.AggregatedResponse is null)
                        return "No aggregated response was produced";
                    if (string.IsNullOrWhiteSpace(resultText))
                        return "Expected non-empty orchestrator response";
                    return null;
                }));
        }

        return new EvalScenarioGroup
        {
            Name = "Orchestrator",
            Description = "Full pipeline routing, multi-agent dispatch, confidence calibration, and fallback behavior",
            Scenarios = scenarios
        };
    }

    private static EvalScenario CreateOrchestratorScenario(
        EvalTestFixture fixture,
        string modelId,
        string embeddingModelId,
        string name,
        string prompt,
        IReadOnlyList<string> tags,
        Func<OrchestratorEvalObserver, string?, string?> validate)
    {
        return new EvalScenario
        {
            Name = $"{name} [{modelId}]",
            Group = "Orchestrator",
            Tags = tags,
            RunAsync = async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var observer = new OrchestratorEvalObserver();
                    var orchestrator = await fixture.CreateLuciaOrchestratorAsync(
                        modelId, observer, embeddingModelId);

                    var result = await orchestrator.ProcessRequestAsync(
                        prompt,
                        taskId: null,
                        sessionId: null,
                        cancellationToken: default);
                    sw.Stop();

                    var failure = validate(observer, result.Text);
                    if (failure is not null)
                        return EvalScenarioResult.Fail(sw.Elapsed, failure, result.Text);

                    var details = $"Routed to: {observer.RoutingDecision?.AgentId} " +
                                  $"(confidence: {observer.RoutingDecision?.Confidence:F2})";
                    return EvalScenarioResult.Pass(sw.Elapsed, details, result.Text);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return EvalScenarioResult.Fail(sw.Elapsed, ex.Message, ex.ToString());
                }
            }
        };
    }

    private static IEnumerable<(string ModelId, string EmbeddingModelId)> GetModelPairs(EvalTestFixture fixture)
    {
        var config = fixture.Configuration;
        if (config.Models.Count == 0)
            return [("gpt-4o", "")];

        var defaultEmbedding = config.EmbeddingModels.Count > 0
            ? config.EmbeddingModels[0].DeploymentName
            : "";

        return config.Models.Select(m => (m.DeploymentName, m.EmbeddingModel ?? defaultEmbedding));
    }
}
