using System.Text.Json;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tui;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Verifies that a test case's <see cref="TestCaseResult.TimedOut"/> flag is carried
/// through every persisted report projection so exported results can distinguish a
/// deadline from an ordinary zero-score failure.
/// </summary>
public sealed class TimedOutReportProjectionTests
{
    private static EvalRunResult BuildResultWithTimeout()
    {
        var perf = ModelPerformanceSummary.FromSnapshots("model", []);
        var timedOutCase = new TestCaseResult
        {
            TestCaseId = "tc-timeout",
            Passed = false,
            Score = 0,
            Latency = TimeSpan.Zero,
            TimedOut = true,
            FailureReason = "Model call timed out"
        };

        return new EvalRunResult
        {
            RunId = "run-1",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            AgentResults =
            [
                new AgentEvalResult
                {
                    AgentName = "agent",
                    ModelResults =
                    [
                        new ModelEvalResult
                        {
                            ModelName = "model",
                            AgentName = "agent",
                            ToolSelectionScore = 0,
                            ToolSuccessScore = 0,
                            ToolEfficiencyScore = 0,
                            TaskCompletionScore = 0,
                            OverallScore = 0,
                            TestCaseCount = 1,
                            PassedCount = 0,
                            Performance = perf,
                            TestCaseResults = [timedOutCase]
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void HtmlReportData_CarriesTimedOut()
    {
        var data = HtmlReportData.FromEvalResult(BuildResultWithTimeout(), new GpuInfo("cpu"));

        var tc = data.Agents[0].Models[0].TestCases![0];
        Assert.True(tc.TimedOut);
    }

    [Fact]
    public void ReportExporter_JsonCarriesTimedOut()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, $"report-test-{Guid.NewGuid():N}");
        try
        {
            var files = ReportExporter.Export(BuildResultWithTimeout(), new GpuInfo("cpu"), dir);
            var jsonPath = files.Single(f => f.EndsWith(".json"));
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));

            var testCase = doc.RootElement
                .GetProperty("agents")[0]
                .GetProperty("models")[0]
                .GetProperty("testCases")[0];
            Assert.True(testCase.GetProperty("timedOut").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TraceExporter_CarriesTimedOut()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, $"trace-test-{Guid.NewGuid():N}");
        try
        {
            var files = TraceExporter.Export(BuildResultWithTimeout(), dir);
            using var doc = JsonDocument.Parse(File.ReadAllText(files.Single()));

            var testCase = doc.RootElement.GetProperty("test_cases")[0];
            Assert.True(testCase.GetProperty("timed_out").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
