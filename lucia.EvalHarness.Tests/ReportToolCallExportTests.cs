using System.Text.Json;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Tui;

namespace lucia.EvalHarness.Tests;

public sealed class ReportToolCallExportTests
{
    [Fact]
    public void ReportExporter_Json_CarriesToolCallsAndAgentOutputForFailureAnalysis()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"report-tools-{Guid.NewGuid():N}");
        try
        {
            var files = ReportExporter.Export(BuildRun(), new GpuInfo("cpu"), directory);
            using var document = JsonDocument.Parse(File.ReadAllText(files.Single(f => f.EndsWith(".json"))));

            var testCase = document.RootElement
                .GetProperty("agents")[0]
                .GetProperty("models")[0]
                .GetProperty("testCases")[0];
            var calls = testCase.GetProperty("toolCalls");

            Assert.Equal("Playing Altered Carbon.", testCase.GetProperty("agentOutput").GetString());
            Assert.Equal("FindPlayer", calls[0].GetProperty("tool").GetString());
            var routing = testCase.GetProperty("routing");
            Assert.Equal("music-agent", routing.GetProperty("agentId").GetString());
            Assert.Equal("Play Altered Carbon", routing.GetProperty("instructions").GetProperty("music-agent").GetString());
            Assert.Equal("PlayAudiobook", calls[1].GetProperty("tool").GetString());
            Assert.Equal("Altered Carbon", calls[1].GetProperty("arguments").GetProperty("audiobook").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static EvalRunResult BuildRun() => new()
    {
        RunId = "run-1",
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        AgentResults =
        [
            new AgentEvalResult
            {
                AgentName = "MusicAgent",
                ModelResults =
                [
                    new ModelEvalResult
                    {
                        ModelName = "model",
                        AgentName = "MusicAgent",
                        ToolSelectionScore = 100,
                        ToolSuccessScore = 100,
                        ToolEfficiencyScore = 100,
                        TaskCompletionScore = 100,
                        OverallScore = 100,
                        TestCaseCount = 1,
                        PassedCount = 1,
                        Performance = ModelPerformanceSummary.FromSnapshots("model", []),
                        TestCaseResults =
                        [
                            new TestCaseResult
                            {
                                TestCaseId = "play_audiobook",
                                Passed = true,
                                Score = 100,
                                Latency = TimeSpan.FromSeconds(1),
                                AgentOutput = "Playing Altered Carbon.",
                                Routing = new RoutingTrace
                                {
                                    AgentId = "music-agent",
                                    Confidence = 0.95,
                                    Instructions = new Dictionary<string, string> { ["music-agent"] = "Play Altered Carbon" },
                                },
                                ToolCalls =
                                [
                                    new ToolCallTrace { ToolName = "FindPlayer", Order = 0 },
                                    new ToolCallTrace
                                    {
                                        ToolName = "PlayAudiobook",
                                        Order = 1,
                                        Arguments = new Dictionary<string, object?> { ["audiobook"] = "Altered Carbon" }
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        ]
    };
}
