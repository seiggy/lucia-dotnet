using System.Text.Json;
using AgentEval.Core;
using AgentEval.Models;
using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Providers;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.EvalHarness.Tui;
using Spectre.Console;

namespace lucia.EvalHarness.Tests;

public sealed class ReportAvailabilityTests
{
    [Fact]
    public void Export_UnavailableJudge_RendersAndSerializesExplicitly()
    {
        var directory = CreateDirectory();
        try
        {
            var paths = ReportExporter.Export(CreateRun(), new GpuInfo("test"), directory);
            var markdown = File.ReadAllText(paths.Single(path => path.EndsWith(".md", StringComparison.Ordinal)));
            var json = File.ReadAllText(paths.Single(path => path.EndsWith(".json", StringComparison.Ordinal)));

            Assert.Contains("N/A (provider_error)", markdown, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(json);
            var model = document.RootElement.GetProperty("agents")[0].GetProperty("models")[0];
            Assert.False(model.TryGetProperty("taskCompletionScore", out _));
            Assert.Equal(JudgeAvailability.ProviderError, model.GetProperty("taskCompletionStatus").GetString());
            Assert.Equal(100, model.GetProperty("overallScore").GetDouble());
            Assert.False(model.TryGetProperty("modelParameters", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Generate_UnavailableJudge_EmbedsStatusForTemplate()
    {
        var directory = CreateDirectory();
        try
        {
            var path = HtmlReportGenerator.Generate(CreateRun(), new GpuInfo("test"), directory);
            var html = File.ReadAllText(path);

            Assert.Contains("\"taskCompletionStatus\":\"provider_error\"", html, StringComparison.Ordinal);
            Assert.Contains("taskCompletionStatus", html, StringComparison.Ordinal);
            Assert.Contains("N/A", html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Export_GenuineZero_PreservesNumericWireFields()
    {
        var directory = CreateDirectory();
        try
        {
            var run = CreateRun(EvalResultFactory.Create(0, taskCompletionScore: 0));
            var paths = ReportExporter.Export(run, new GpuInfo("test"), directory);
            var json = File.ReadAllText(paths.Single(path => path.EndsWith(".json", StringComparison.Ordinal)));

            using var document = JsonDocument.Parse(json);
            var model = document.RootElement.GetProperty("agents")[0].GetProperty("models")[0];
            Assert.Equal(0, model.GetProperty("overallScore").GetDouble());
            Assert.Equal(100, model.GetProperty("toolSelectionScore").GetDouble());
            Assert.Equal(100, model.GetProperty("toolSuccessScore").GetDouble());
            Assert.Equal(100, model.GetProperty("toolEfficiencyScore").GetDouble());
            Assert.Equal(0, model.GetProperty("taskCompletionScore").GetDouble());
            Assert.False(model.TryGetProperty("taskCompletionStatus", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Exporters_AllUnavailable_DoNotRenderValidResultOrWinner()
    {
        var directory = CreateDirectory();
        try
        {
            var runner = new EvalRunner(new HarnessConfiguration(), null)
            {
                TestContextFactory = (_, _, _) =>
                    Task.FromException<(EvaluationContext, PerformanceSnapshot)>(
                        new HttpRequestException("provider unavailable"))
            };
            var agent = new RealAgentInstance
            {
                AgentName = "unavailable-model",
                Agent = A.Fake<ILuciaAgent>(),
                DatasetFile = "unused"
            };
            var modelResult = await runner.EvaluateRealAgentAsync(
                "unavailable-model",
                agent,
                [new TestCase { Name = "case", Input = "input" }]);
            var run = CreateRun(modelResult);

            var htmlPath = HtmlReportGenerator.Generate(run, new GpuInfo("test"), directory);
            var html = File.ReadAllText(htmlPath);
            Assert.Contains("filter(x => x.avgScore != null)", html, StringComparison.Ordinal);
            Assert.Contains("filter(x => x.avgLatency != null)", html, StringComparison.Ordinal);
            Assert.Contains("['Overall', model.overallScore, model.overallScoreStatus]", html, StringComparison.Ordinal);
            Assert.Contains("${fmtScore(val, status)}", html, StringComparison.Ordinal);

            var reportPath = ReportExporter.Export(run, new GpuInfo("test"), directory)
                .Single(path => path.EndsWith(".json", StringComparison.Ordinal));
            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            var reportModel = report.RootElement.GetProperty("agents")[0].GetProperty("models")[0];
            Assert.False(reportModel.TryGetProperty("toolSelectionScore", out _));
            Assert.False(reportModel.TryGetProperty("toolSuccessScore", out _));
            Assert.False(reportModel.TryGetProperty("toolEfficiencyScore", out _));

            var tracePath = TraceExporter.Export(run, directory).Single();
            using var trace = JsonDocument.Parse(File.ReadAllText(tracePath));
            var summary = trace.RootElement.GetProperty("summary");
            Assert.False(summary.TryGetProperty("overall_score", out _));
            Assert.Equal(
                JudgeAvailability.Unavailable,
                summary.GetProperty("overall_score_status").GetString());
            Assert.False(summary.TryGetProperty("mean_latency_ms", out _));

            var previousConsole = AnsiConsole.Console;
            using var writer = new StringWriter();
            AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer)
            });
            try
            {
                TraceExporter.RenderTraceSummary(run);
            }
            finally
            {
                AnsiConsole.Console = previousConsole;
            }

            Assert.Contains("score: N/A", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("score: 0", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static EvalRunResult CreateRun(ModelEvalResult? modelResult = null) =>
        new()
        {
            RunId = "run",
            StartedAt = DateTimeOffset.UnixEpoch,
            CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            AgentResults =
            [
                new AgentEvalResult
                {
                    AgentName = "agent",
                    ModelResults =
                    [
                        modelResult ?? EvalResultFactory.Create(
                            100,
                            taskCompletionScore: null,
                            taskCompletionStatus: JudgeAvailability.ProviderError)
                    ]
                }
            ]
        };

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "report-availability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
