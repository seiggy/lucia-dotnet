using System.Text.Json;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tests.TestDoubles;
using lucia.EvalHarness.Tui;

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
            Assert.Equal(JsonValueKind.Null, model.GetProperty("taskCompletionScore").ValueKind);
            Assert.Equal(JudgeAvailability.ProviderError, model.GetProperty("taskCompletionStatus").GetString());
            Assert.Equal(100, model.GetProperty("overallScore").GetDouble());
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
            Assert.Equal(0, model.GetProperty("taskCompletionScore").GetDouble());
            Assert.Equal(JsonValueKind.Null, model.GetProperty("taskCompletionStatus").ValueKind);
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
