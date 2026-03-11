using System.Diagnostics;
using AgentEval.Core;
using AgentEval.MAF;
using AgentEval.Models;

namespace lucia.Evals;

/// <summary>
/// Music agent evaluation scenarios — tests tool call accuracy,
/// intent resolution, and task adherence for music playback control.
/// </summary>
public sealed class MusicAgentScenarios
{
    public static EvalScenarioGroup Create(EvalTestFixture fixture)
    {
        var scenarios = new List<EvalScenario>();

        foreach (var (modelId, embeddingModelId) in GetModelPairs(fixture))
        {
            // ── Tool Call Accuracy ────────────────────────────────────

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "PlayArtist",
                prompt: "Play The Cure on the kitchen speaker",
                tags: ["music", "artist", "tool-accuracy"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "PlayAlbum",
                prompt: "Play the album Random Access Memories on the office speaker",
                tags: ["music", "album", "tool-accuracy"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "PlaySong",
                prompt: "Play Shivers by Ed Sheeran in the bedroom",
                tags: ["music", "song", "tool-accuracy"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "Shuffle",
                prompt: "Just shuffle some music on the loft speaker",
                tags: ["music", "shuffle", "tool-accuracy"],
                expectToolCalls: true));

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "StopMusic",
                prompt: "Stop the music in the kitchen",
                tags: ["music", "stop", "tool-accuracy"],
                expectToolCalls: true));

            // ── Intent Resolution ─────────────────────────────────────

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "PlayGenre",
                prompt: "Play some relaxed jazz on the Satellite1 kitchen",
                tags: ["music", "genre", "intent-resolution"],
                expectToolCalls: true));

            // ── Task Adherence ────────────────────────────────────────

            scenarios.Add(CreateMafScenario(
                fixture, modelId, embeddingModelId,
                name: "OutOfDomain",
                prompt: "Turn on the living room lights",
                tags: ["music", "out-of-domain", "task-adherence"],
                expectToolCalls: false));
        }

        return new EvalScenarioGroup
        {
            Name = "Music Agent",
            Description = "Tool accuracy, intent resolution, and task adherence for music playback",
            Scenarios = scenarios
        };
    }

    private static EvalScenario CreateMafScenario(
        EvalTestFixture fixture,
        string modelId,
        string embeddingModelId,
        string name,
        string prompt,
        IReadOnlyList<string> tags,
        bool expectToolCalls)
    {
        return new EvalScenario
        {
            Name = $"{name} [{modelId}]",
            Group = "Music Agent",
            Tags = tags,
            RunAsync = async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var agent = await fixture.CreateMusicAgentAsync(modelId, embeddingModelId);
                    var harness = new MAFEvaluationHarness(verbose: false);
                    var adapter = new MAFAgentAdapter(agent.GetAIAgent());

                    var testCase = new TestCase
                    {
                        Name = $"MusicAgent.{name}",
                        Input = prompt
                    };

                    var result = await harness.RunEvaluationAsync(adapter, testCase,
                        options: new EvaluationOptions { TrackTools = true });
                    sw.Stop();

                    if (string.IsNullOrWhiteSpace(result.ActualOutput))
                        return EvalScenarioResult.Fail(sw.Elapsed, "No response from agent");

                    if (expectToolCalls && !result.ToolsWereCalled)
                        return EvalScenarioResult.Fail(sw.Elapsed,
                            $"Expected tool calls for {name} but none were made",
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
