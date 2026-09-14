using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using lucia.EvalHarness.Reports;
using lucia.EvalHarness.Personality;
using lucia.EvalHarness.Tui;
using System.Text.Json;

namespace lucia.EvalHarness.Tests;

public sealed class CostRecommendationTests
{
    [Fact]
    public void Formatting_DoesNotRoundSmallKnownChargesToFree()
    {
        Assert.Equal("$0.00000000000000000001", CostReportFormatting.Usd(0.00000000000000000001m));
    }

    [Fact]
    public void Ranking_QualityFirstThenCheapestKnownMeanTestCost()
    {
        var results = Run(
            Model("unknown", 90, null),
            Model("paid", 90, 0.01m),
            Model("local", 90, 0m),
            Model("best-quality", 91, 1m),
            Model("unavailable", null, 0m));

        Assert.Equal(["best-quality", "local", "paid", "unknown"],
            ModelRecommendation.Rank(results).Select(result => result.ModelName));
    }

    [Fact]
    public void Ranking_UsesMeanPerTestNotDifferentExecutionCounts()
    {
        var result = Run(Model("few-tests", 90, 0.02m), Model("many-tests", 90, 0.01m, 3));
        Assert.Equal("many-tests", ModelRecommendation.Rank(result)[0].ModelName);
    }

    [Fact]
    public void Sweep_PreservesVarianceBeforeCostAndUsesCostToBreakRemainingTies()
    {
        var paid = Entry(Model("paid", 90, 0.01m));
        var local = Entry(Model("local", 90, 0m));
        Assert.Same(local, SweepRunAggregator.SelectWinner([paid, local]));

        var variableLocal = new SweepEntry
        {
            Profile = ModelParameterProfile.Default,
            Results = [Model("local", 85, 0m)],
            AllRunResults = [[Model("local", 85, 0m)], [Model("local", 95, 0m)]]
        };
        Assert.Same(paid, SweepRunAggregator.SelectWinner([variableLocal, paid]));
    }

    [Fact]
    public void HtmlProjection_ContainsPerTestAndAggregateCostsAndServerRankedWinner()
    {
        var result = Run(Model("paid", 90, 0.01m), Model("local", 90, 0m));
        var html = HtmlReportData.FromEvalResult(result, new GpuInfo("CPU", null, null, null, "test"));
        Assert.Equal(0.01m, html.Agents[0].Models[0].Cost.EstimatedUsd);
        Assert.Equal(10, html.Agents[0].Models[0].TestCases[0].Cost.InputTokens);
        Assert.Equal("local", html.Recommendations[0].ModelName);
    }

    [Fact]
    public void Export_ContainsExactUsdAndExplicitUnknownAndTokensInAllFormats()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", $"cost-test-{Guid.NewGuid():N}");
        try
        {
            var result = Run(Model("paid", 90, 0.0000000123m), Model("unknown", 90, null));
            var paths = ReportExporter.Export(result, new GpuInfo("test"), directory);
            var markdown = File.ReadAllText(paths.Single(path => path.EndsWith(".md")));
            Assert.Contains("$0.0000000123", markdown);
            Assert.Contains("Unknown (missing_pricing)", markdown);
            Assert.Contains("10 / 2 / ?", markdown);

            using var document = JsonDocument.Parse(File.ReadAllText(paths.Single(path => path.EndsWith(".json"))));
            var models = document.RootElement.GetProperty("agents")[0].GetProperty("models");
            Assert.Equal(0.0000000123m, models[0].GetProperty("cost").GetProperty("EstimatedUsd").GetDecimal());
            Assert.Equal(JsonValueKind.Null, models[1].GetProperty("cost").GetProperty("EstimatedUsd").ValueKind);
            Assert.Equal(10, models[0].GetProperty("testCases")[0].GetProperty("cost").GetProperty("InputTokens").GetInt64());
            var html = File.ReadAllText(HtmlReportGenerator.Generate(result, new GpuInfo("test"), directory));
            Assert.Contains("Tokens in / out / cached", html);
            Assert.Contains("\"EstimatedUsd\":0.0000000123", html);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void PersonalityExport_QualityFirstAndCostsAvailableAcrossFormats()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", $"personality-cost-test-{Guid.NewGuid():N}");
        try
        {
            var cloud = Personality("paid", 0.01m);
            var local = Personality("local", 0m);
            Assert.Equal("local", PersonalityCostReport.Rank([cloud, local])[0].ModelName);
            var paths = PersonalityCostReport.Export([cloud, local], directory);
            Assert.Equal(3, paths.Count);
            foreach (var path in paths)
            {
                var content = File.ReadAllText(path);
                Assert.Contains("local", content);
                Assert.Contains("0.01", content);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static PersonalityEvalReport Personality(string model, decimal usd) => new()
    {
        ModelName = model,
        JudgeModelName = "judge",
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Results = [new()
        {
            ScenarioId = "case",
            ScenarioDescription = "Cost report",
            Category = "test",
            ProfileId = "plain",
            ProfileName = "Plain",
            ModelName = model,
            Score = 5,
            JudgeResult = new JudgeResult { PersonalityScore = 5, MeaningScore = 5 },
            LlmResponse = "done",
            DurationMs = 1,
            Cost = new() { CallCount = 1, InputTokens = 10, OutputTokens = 2, EstimatedUsd = usd, CostStatus = usd == 0m ? "local" : "estimated" }
        }]
    };

    private static SweepEntry Entry(ModelEvalResult model) => new()
    {
        Profile = ModelParameterProfile.Default,
        Results = [model],
        AllRunResults = [[model]]
    };

    internal static EvalRunResult Run(params ModelEvalResult[] models) => new()
    {
        RunId = "cost-test",
        StartedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        AgentResults = [new AgentEvalResult { AgentName = "agent", ModelResults = models }]
    };

    internal static ModelEvalResult Model(string name, double? score, decimal? cost, int tests = 1) => new()
    {
        ModelName = name,
        AgentName = "agent",
        ToolSelectionScore = score,
        ToolSuccessScore = score,
        ToolEfficiencyScore = score,
        TaskCompletionScore = score,
        OverallScore = score,
        TestCaseCount = tests,
        ScoredTestCaseCount = score.HasValue ? tests : 0,
        PassedCount = score >= 70 ? tests : 0,
        Performance = ModelPerformanceSummary.FromSnapshots(name, []),
        TestCaseResults = Enumerable.Range(0, tests).Select(index => new TestCaseResult
        {
            TestCaseId = $"case-{index}",
            Passed = score >= 70,
            Score = score,
            Latency = TimeSpan.Zero,
            Cost = new InferenceCostSummary
            {
                CallCount = 1,
                InputTokens = 10,
                OutputTokens = 2,
                EstimatedUsd = cost,
                CostStatus = cost.HasValue ? cost == 0m ? "local" : "estimated" : "missing_pricing",
                UsageStatus = "available"
            }
        }).ToList()
    };
}
