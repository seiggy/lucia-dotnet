using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Fast, provider-free tests for parameter-sweep aggregation logic.
/// These tests verify that each combination is run N times and that the winner
/// is selected on mean score across all runs rather than a single lucky sample.
/// </summary>
public sealed class ParameterSweepAggregationTests
{
    // ──────────────────────────────────────────────────────────────
    // ParameterSweepConfig defaults
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void RunsPerCombination_DefaultsToThree()
    {
        var config = new ParameterSweepConfig();
        Assert.Equal(3, config.RunsPerCombination);
    }

    [Fact]
    public void RunsPerCombination_CanBeConfigured()
    {
        var config = new ParameterSweepConfig { RunsPerCombination = 5 };
        Assert.Equal(5, config.RunsPerCombination);
    }

    // ──────────────────────────────────────────────────────────────
    // SweepRunAggregator.ComputeMean
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeMean_ReturnsZero_WhenNoRuns()
    {
        var mean = SweepRunAggregator.ComputeMean(new List<IReadOnlyList<ModelEvalResult>>());
        Assert.Equal(0.0, mean);
    }

    [Fact]
    public void ComputeMean_SingleRun_EqualsRunScore()
    {
        var allRuns = Runs(Run(80.0));
        var mean = SweepRunAggregator.ComputeMean(allRuns);
        Assert.Equal(80.0, mean);
    }

    [Fact]
    public void ComputeMean_MultipleRuns_ReturnsAcrossAllRunsAndAgents()
    {
        // 3 runs: 60, 80, 100 -> mean = 80
        var allRuns = Runs(Run(60.0), Run(80.0), Run(100.0));
        var mean = SweepRunAggregator.ComputeMean(allRuns);
        Assert.Equal(80.0, mean, precision: 6);
    }

    [Fact]
    public void ComputeMean_MultipleAgentsPerRun_AveragesAcrossAll()
    {
        // 2 runs x 2 agents: (50+70) + (60+80) = 260 / 4 = 65
        var allRuns = new List<IReadOnlyList<ModelEvalResult>>
        {
            new List<ModelEvalResult> { MakeResult(50.0), MakeResult(70.0) },
            new List<ModelEvalResult> { MakeResult(60.0), MakeResult(80.0) }
        };
        var mean = SweepRunAggregator.ComputeMean(allRuns);
        Assert.Equal(65.0, mean, precision: 6);
    }

    // ──────────────────────────────────────────────────────────────
    // SweepRunAggregator.ComputeMinRunMean
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeMinRunMean_ReturnsZero_WhenNoRuns()
    {
        var minMean = SweepRunAggregator.ComputeMinRunMean(new List<IReadOnlyList<ModelEvalResult>>());
        Assert.Equal(0.0, minMean);
    }

    [Fact]
    public void ComputeMinRunMean_SingleRun_ReturnsRunMean()
    {
        var minMean = SweepRunAggregator.ComputeMinRunMean(Runs(Run(73.0)));
        Assert.Equal(73.0, minMean, precision: 6);
    }

    [Fact]
    public void ComputeMinRunMean_MultipleRuns_ReturnsLowestPerRunMean()
    {
        // 3 runs with means 60, 80, 100 — min is 60
        var minMean = SweepRunAggregator.ComputeMinRunMean(Runs(Run(100.0), Run(60.0), Run(80.0)));
        Assert.Equal(60.0, minMean, precision: 6);
    }

    // ──────────────────────────────────────────────────────────────
    // SweepRunAggregator.ComputeVariance
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeVariance_SingleRun_ReturnsZero()
    {
        var variance = SweepRunAggregator.ComputeVariance(Runs(Run(70.0)));
        Assert.Equal(0.0, variance);
    }

    [Fact]
    public void ComputeVariance_IdenticalRuns_ReturnsZero()
    {
        var allRuns = Runs(Run(75.0), Run(75.0), Run(75.0));
        var variance = SweepRunAggregator.ComputeVariance(allRuns);
        Assert.Equal(0.0, variance, precision: 6);
    }

    [Fact]
    public void ComputeVariance_VaryingRuns_IsPositive()
    {
        var allRuns = Runs(Run(50.0), Run(100.0));
        var variance = SweepRunAggregator.ComputeVariance(allRuns);
        Assert.True(variance > 0, "Variance of 50 and 100 should be positive");
    }

    // ──────────────────────────────────────────────────────────────
    // SweepRunAggregator.SelectWinner
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SelectWinner_PicksHighestMean_NotSingleRunWinner()
    {
        // Profile A: single noisy run of 95, but mean is (95+40+40)/3 = 58.3
        // Profile B: steady runs of 75, mean = 75 -> B is the true winner
        var entryA = MakeSweepEntry(MakeProfile("A"), 95.0, 40.0, 40.0);
        var entryB = MakeSweepEntry(MakeProfile("B"), 75.0, 75.0, 75.0);

        var winner = SweepRunAggregator.SelectWinner(new List<SweepEntry> { entryA, entryB });

        Assert.Equal("B", winner.Profile.Name);
    }

    [Fact]
    public void SelectWinner_TieOnMean_PrefersLowerVariance()
    {
        // Both have mean 75, but A is volatile (50/100) while B is stable (75/75)
        var entryA = MakeSweepEntry(MakeProfile("A"), 50.0, 100.0);
        var entryB = MakeSweepEntry(MakeProfile("B"), 75.0, 75.0);

        var winner = SweepRunAggregator.SelectWinner(new List<SweepEntry> { entryA, entryB });

        Assert.Equal("B", winner.Profile.Name);
    }

    [Fact]
    public void SelectWinner_SingleEntry_ReturnsThatEntry()
    {
        var entry = MakeSweepEntry(MakeProfile("only"), 65.0);
        var winner = SweepRunAggregator.SelectWinner(new List<SweepEntry> { entry });
        Assert.Equal("only", winner.Profile.Name);
    }

    // ──────────────────────────────────────────────────────────────
    // SweepRunAggregator.DeriveRunSeed
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void DeriveRunSeed_NoBaseSeed_ReturnsNull()
    {
        Assert.Null(SweepRunAggregator.DeriveRunSeed(null, 0));
        Assert.Null(SweepRunAggregator.DeriveRunSeed(null, 2));
    }

    [Fact]
    public void DeriveRunSeed_WithBaseSeed_OffsetsByRunIndex()
    {
        Assert.Equal(42, SweepRunAggregator.DeriveRunSeed(42, 0));
        Assert.Equal(43, SweepRunAggregator.DeriveRunSeed(42, 1));
        Assert.Equal(44, SweepRunAggregator.DeriveRunSeed(42, 2));
    }

    [Fact]
    public void DeriveRunSeed_DifferentRuns_ProduceDifferentSeeds()
    {
        var distinctCount = Enumerable.Range(0, 5)
            .Select(i => SweepRunAggregator.DeriveRunSeed(100, i))
            .Distinct()
            .Count();
        Assert.Equal(5, distinctCount);
    }

    // ──────────────────────────────────────────────────────────────
    // SweepEntry computed properties
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void SweepEntry_MeanScore_ReflectsAllRuns()
    {
        var entry = MakeSweepEntry(MakeProfile("test"), 60.0, 80.0, 100.0);
        Assert.Equal(80.0, entry.MeanScore, precision: 6);
    }

    [Fact]
    public void SweepEntry_AverageScore_IsAliasForMeanScore()
    {
        var entry = MakeSweepEntry(MakeProfile("test"), 55.0, 85.0);
        Assert.Equal(entry.MeanScore, entry.AverageScore);
    }

    [Fact]
    public void SweepEntry_Results_IsFirstRun()
    {
        var firstResult = MakeResult(42.0);
        var allRuns = new List<IReadOnlyList<ModelEvalResult>>
        {
            new List<ModelEvalResult> { firstResult },
            Run(90.0)
        };

        var entry = new SweepEntry
        {
            Profile = MakeProfile("test"),
            Results = allRuns[0],
            AllRunResults = allRuns
        };

        Assert.Same(firstResult, entry.Results[0]);
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static ModelEvalResult MakeResult(double score, string agentName = "test-agent") =>
        new()
        {
            ModelName = "test-model",
            AgentName = agentName,
            ToolSelectionScore = score,
            ToolSuccessScore = score,
            ToolEfficiencyScore = score,
            TaskCompletionScore = score,
            OverallScore = score,
            TestCaseCount = 1,
            PassedCount = 1,
            Performance = ModelPerformanceSummary.FromSnapshots("test-model", new List<PerformanceSnapshot>()),
            TestCaseResults = new List<TestCaseResult>()
        };

    private static ModelParameterProfile MakeProfile(string name) =>
        new() { Name = name };

    private static IReadOnlyList<ModelEvalResult> Run(double score) =>
        new List<ModelEvalResult> { MakeResult(score) };

    private static List<IReadOnlyList<ModelEvalResult>> Runs(params IReadOnlyList<ModelEvalResult>[] runs) =>
        new List<IReadOnlyList<ModelEvalResult>>(runs);

    /// <summary>
    /// Creates a SweepEntry where each score becomes a single-agent run.
    /// The first run populates Results; all runs populate AllRunResults.
    /// </summary>
    private static SweepEntry MakeSweepEntry(ModelParameterProfile profile, params double[] runScores)
    {
        var allRuns = runScores
            .Select(score => (IReadOnlyList<ModelEvalResult>)new List<ModelEvalResult> { MakeResult(score) })
            .ToList();

        return new SweepEntry
        {
            Profile = profile,
            Results = allRuns[0],
            AllRunResults = allRuns
        };
    }
}
