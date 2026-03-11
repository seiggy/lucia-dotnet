using System.Diagnostics;
using AgentEval.Core;
using AgentEval.MAF;
using AgentEval.Models;

namespace lucia.Evals;

/// <summary>
/// Light agent evaluation scenarios — tests tool call accuracy,
/// intent resolution with STT variants, and task adherence.
/// </summary>
public sealed class LightAgentScenarios
{
    public static EvalScenarioGroup Create(EvalTestFixture fixture)
    {
        var scenarios = new List<EvalScenario>();

        foreach (var (modelId, embeddingModelId) in GetModelPairs(fixture))
        {
            // ── Tool Call Accuracy ────────────────────────────────────

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "FindLight",
                prompt: "Turn on the kitchen light",
                variant: "exact",
                tags: ["light", "find", "exact", "tool-accuracy"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "FindLightsByArea",
                prompt: "Turn off all the lights in the living room",
                variant: "exact",
                tags: ["light", "area", "exact", "tool-accuracy"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "GetLightState",
                prompt: "What is the status of the hallway light?",
                variant: "exact",
                tags: ["light", "state", "exact", "tool-accuracy"],
                expectToolCalls: true));

            // ── Intent Resolution: DimLight STT variants ─────────────

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "DimLight",
                prompt: "Dim Zack\u2019s Light to 50%",
                variant: "exact",
                tags: ["light", "dim", "exact", "intent-resolution"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "DimLight",
                prompt: "Dim Zach's Light to 50%",
                variant: "stt-spelling",
                tags: ["light", "dim", "stt", "intent-resolution"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "DimLight",
                prompt: "Dim Sack's Light to 50%",
                variant: "stt-lisp-sack",
                tags: ["light", "dim", "stt", "intent-resolution"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "DimLight",
                prompt: "Dim Zagslight to 50%",
                variant: "stt-lisp-zaglight",
                tags: ["light", "dim", "stt", "intent-resolution"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "DimLight",
                prompt: "Dim Sag's Light to 50%",
                variant: "stt-lisp-sag",
                tags: ["light", "dim", "stt", "intent-resolution"],
                expectToolCalls: true));

            // ── Intent Resolution: SetColor ───────────────────────────

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "SetColor",
                prompt: "Set the kitchen lights to blue",
                variant: "exact",
                tags: ["light", "color", "exact", "intent-resolution"],
                expectToolCalls: true));

            // ── Task Adherence: OutOfDomain ───────────────────────────

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "OutOfDomain",
                prompt: "Play some jazz music in the living room",
                variant: "music-request",
                tags: ["light", "out-of-domain", "task-adherence"],
                expectToolCalls: false));
        }

        return new EvalScenarioGroup
        {
            Name = "Light Agent",
            Description = "Tool accuracy, intent resolution, and task adherence for light control",
            Scenarios = scenarios
        };
    }

    private static EvalScenario CreateMafScenario(
        EvalTestFixture fixture,
        string modelId,
        string embeddingModelId,
        string name,
        string prompt,
        string variant,
        IReadOnlyList<string> tags,
        bool expectToolCalls)
    {
        return new EvalScenario
        {
            Name = $"{name} [{variant}] [{modelId}]",
            Group = "Light Agent",
            Tags = tags,
            RunAsync = async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var agent = await fixture.CreateLightAgentAsync(modelId, embeddingModelId);
                    var harness = new MAFEvaluationHarness(verbose: false);
                    var adapter = new MAFAgentAdapter(agent.GetAIAgent());

                    var testCase = new TestCase
                    {
                        Name = $"LightAgent.{name}[{variant}]",
                        Input = prompt
                    };

                    var result = await harness.RunEvaluationAsync(adapter, testCase,
                        options: new EvaluationOptions { TrackTools = true });
                    sw.Stop();

                    if (string.IsNullOrWhiteSpace(result.ActualOutput))
                        return EvalScenarioResult.Fail(sw.Elapsed, "No response from agent");

                    if (expectToolCalls && !result.ToolsWereCalled)
                        return EvalScenarioResult.Fail(sw.Elapsed,
                            $"Expected tool calls for {name} ({variant}) but none were made",
                            result.ActualOutput);

                    return EvalScenarioResult.Pass(sw.Elapsed,
                        $"Agent responded{(result.ToolsWereCalled ? " with tool calls" : "")}",
                        result.ActualOutput);
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
