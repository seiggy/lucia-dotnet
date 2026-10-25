using System.Text.Json;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Tui;

namespace lucia.EvalHarness.Tests;

public sealed class ContextBudgetReportTests
{
    [Fact]
    public void JsonAndMarkdown_IncludeEveryProfileBudgetForTheSameModel()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucia-context-report-{Guid.NewGuid():N}");
        try
        {
            var paths = ReportExporter.Export(Run(), new GpuInfo("test"), directory);
            using var json = JsonDocument.Parse(File.ReadAllText(paths.Single(path => path.EndsWith(".json"))));
            var models = json.RootElement.GetProperty("agents")[0].GetProperty("models");
            Assert.Equal(8192, models[0].GetProperty("modelParameters").GetProperty("numContext").GetInt32());
            Assert.Equal(16384, models[1].GetProperty("modelParameters").GetProperty("numContext").GetInt32());
            var markdown = File.ReadAllText(paths.Single(path => path.EndsWith(".md")));
            Assert.Contains("Requested context", markdown);
            Assert.Contains("8192", markdown);
            Assert.Contains("16384", markdown);
            Assert.Contains("512", markdown);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void HtmlData_PreservesBudgetsForModelsAndProfileComparison()
    {
        var data = JsonSerializer.SerializeToElement(HtmlReportData.FromEvalResult(Run(), new GpuInfo("test")));
        var models = data.GetProperty("agents")[0].GetProperty("models");
        Assert.Equal(8192, models[0].GetProperty("parameters").GetProperty("numContext").GetInt32());
        Assert.Equal(16384, models[1].GetProperty("parameters").GetProperty("numContext").GetInt32());
        foreach (var profile in data.GetProperty("profileComparison")[0].GetProperty("profiles").EnumerateArray())
        {
            Assert.True(profile.GetProperty("parameters").GetProperty("numContext").GetInt32() > 0);
            Assert.Equal(512, profile.GetProperty("parameters").GetProperty("numPredict").GetInt32());
        }
    }

    [Fact]
    public void HtmlReport_LabelsBudgetAsRequestedNotObserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lucia-context-html-{Guid.NewGuid():N}");
        try
        {
            var path = HtmlReportGenerator.Generate(Run(), new GpuInfo("test"), directory);
            var html = File.ReadAllText(path);
            Assert.Contains("Requested context", html);
            Assert.Contains("pp.numContext", html);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static EvalRunResult Run() => new()
    {
        RunId = "context-test",
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        AgentResults = [new AgentEvalResult { AgentName = "agent", ModelResults = [Model(8192), Model(16384)] }]
    };

    private static ModelEvalResult Model(int budget) => new()
    {
        ModelName = "same-model",
        AgentName = "agent",
        ParameterProfile = new ModelParameterProfile { Name = $"ctx-{budget}", NumContext = budget, NumPredict = 512 },
        OverallScore = 100,
        ToolSelectionScore = 100,
        ToolSuccessScore = 100,
        ToolEfficiencyScore = 100,
        TaskCompletionScore = null,
        TestCaseCount = 0,
        PassedCount = 0,
        TestCaseResults = [],
        Performance = ModelPerformanceSummary.FromSnapshots("same-model", [])
    };
}
