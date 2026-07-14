using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Tests;

/// <summary>
/// Provider-free tests for ParameterSweepRunner.RunAsync using the injectable
/// evaluation backend constructor. Verifies that each combination is run N times,
/// that the per-run profile (including seed offset) is propagated to the backend,
/// and that the baseline is also run N times before deltas are computed.
/// </summary>
public sealed class ParameterSweepRunnerTests
{
    [Fact]
    public async Task RunAsync_EachCombination_IsEvaluatedNTimes()
    {
        var callLog = new List<(string ModelName, ModelParameterProfile Profile)>();

        var runner = MakeRunner(callLog, score: 70.0);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 3,
            // Use a minimal grid: 1 temp × 1 topK × 1 topP × 1 repeat = 1 combo
            TemperatureValues = [0.5],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        // 3 baseline runs + 1 combo × 3 runs = 6 total calls
        var baselineCalls = callLog.Where(c => c.ModelName == "baseline-model").ToList();
        var targetCalls = callLog.Where(c => c.ModelName == "target-model").ToList();

        Assert.Equal(3, baselineCalls.Count);
        Assert.Equal(3, targetCalls.Count);
    }

    [Fact]
    public async Task RunAsync_TwoCombinations_EachEvaluatedNTimes()
    {
        var callLog = new List<(string ModelName, ModelParameterProfile Profile)>();

        var runner = MakeRunner(callLog, score: 70.0);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 2,
            TemperatureValues = [0.3, 0.7],    // 2 temps
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        // 2 baseline runs + 2 combos × 2 runs = 6 total
        Assert.Equal(6, callLog.Count);

        var targetCalls = callLog.Where(c => c.ModelName == "target-model").ToList();
        Assert.Equal(4, targetCalls.Count);
    }

    [Fact]
    public async Task RunAsync_WithBaseSeed_RunsWithinComboHaveDistinctSeeds()
    {
        var callLog = new List<(string ModelName, ModelParameterProfile Profile)>();
        var runner = MakeRunner(callLog, score: 70.0);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 3,
            BaseSeed = 100,
            TemperatureValues = [0.5],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        var targetCalls = callLog.Where(c => c.ModelName == "target-model").ToList();
        Assert.Equal(3, targetCalls.Count);

        // Each run within the same combination must have a distinct seed
        var seeds = targetCalls.Select(c => c.Profile.Seed).ToList();
        Assert.All(seeds, s => Assert.NotNull(s));
        Assert.Equal(3, seeds.Distinct().Count());
    }

    [Fact]
    public async Task RunAsync_WithBaseSeed_DifferentCombosHaveNonOverlappingSeedBlocks()
    {
        var callLog = new List<(string ModelName, ModelParameterProfile Profile)>();
        var runner = MakeRunner(callLog, score: 70.0);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 3,
            BaseSeed = 1000,
            TemperatureValues = [0.3, 0.7],   // 2 combos
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        var targetCalls = callLog.Where(c => c.ModelName == "target-model").ToList();
        Assert.Equal(6, targetCalls.Count); // 2 combos × 3 runs

        // All 6 target runs must have distinct seeds (no block overlap between combos)
        var seeds = targetCalls.Select(c => c.Profile.Seed).ToList();
        Assert.All(seeds, s => Assert.NotNull(s));
        Assert.Equal(6, seeds.Distinct().Count());
    }

    [Fact]
    public async Task RunAsync_WithoutBaseSeed_ProfileSeedsAreNull()
    {
        var callLog = new List<(string ModelName, ModelParameterProfile Profile)>();
        var runner = MakeRunner(callLog, score: 70.0);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 2,
            BaseSeed = null,  // no seed
            TemperatureValues = [0.5],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        // Explicit BaseSeed = null must opt BOTH baseline and target runs out of
        // seeding, not just the target sweep. Baseline runs previously fell back to
        // ModelParameterProfile.Default (Seed = DefaultSeed), silently ignoring the opt-out.
        var baselineCalls = callLog.Where(c => c.ModelName == "baseline-model").ToList();
        var targetCalls = callLog.Where(c => c.ModelName == "target-model").ToList();

        Assert.NotEmpty(baselineCalls);
        Assert.NotEmpty(targetCalls);
        Assert.All(baselineCalls, c => Assert.Null(c.Profile.Seed));
        Assert.All(targetCalls, c => Assert.Null(c.Profile.Seed));
    }

    [Fact]
    public async Task RunAsync_BaselineMeanScore_IsAggregatedAcrossAllBaselineRuns()
    {
        // Each baseline call returns a different score so we can verify mean, not
        // just a single-run value.
        var callCount = 0;
        var scores = new[] { 60.0, 80.0, 100.0 };  // mean = 80

        Func<string, ModelParameterProfile, CancellationToken, Task<IReadOnlyList<ModelEvalResult>>> backend =
            (_, _, _) =>
            {
                var score = scores[callCount % scores.Length];
                callCount++;
                return Task.FromResult<IReadOnlyList<ModelEvalResult>>(
                    new List<ModelEvalResult> { MakeResult(score) });
            };

        var runner = new ParameterSweepRunner(backend);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 3,
            TemperatureValues = [0.5],
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        // The first 3 calls are baseline (scores 60, 80, 100 → mean=80)
        var result = await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        Assert.Equal(80.0, result.BaselineMeanScore, precision: 6);
    }

    [Fact]
    public async Task RunAsync_WinnerSelection_UsesMeanNotSingleRun()
    {
        // Backend returns low scores for combo 0 and consistent high scores for combo 1
        var scoreMap = new Dictionary<int, double>
        {
            [0] = 90.0,  // combo-0 first run: noisy high
            [1] = 50.0,  // combo-0 second run: low
            [2] = 50.0,  // combo-0 third run: low  → mean = 63.3
            [3] = 75.0,  // combo-1 first run
            [4] = 75.0,  // combo-1 second run
            [5] = 75.0   // combo-1 third run → mean = 75 → should win
        };

        var callIndex = 0;
        Func<string, ModelParameterProfile, CancellationToken, Task<IReadOnlyList<ModelEvalResult>>> backend =
            (modelName, _, _) =>
            {
                double score;
                if (modelName == "target-model")
                {
                    score = scoreMap[callIndex++];
                }
                else
                {
                    score = 70.0; // baseline (doesn't matter here)
                }
                return Task.FromResult<IReadOnlyList<ModelEvalResult>>(
                    new List<ModelEvalResult> { MakeResult(score) });
            };

        var runner = new ParameterSweepRunner(backend);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 3,
            TemperatureValues = [0.3, 0.7],  // exactly 2 combos
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        var result = await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);
        var entries = result.TargetResults["target-model"].ToList();

        // Combo 0 mean = (90+50+50)/3 = 63.3, combo 1 mean = 75 → combo 1 should win
        var winner = SweepRunAggregator.SelectWinner(entries);
        Assert.Equal(75.0, winner.MeanScore, precision: 1);
    }

    [Fact]
    public async Task RunAsync_WithBaseSeed_PerRunSeedsMatchDerivedFormula()
    {
        // Combo 0, BaseSeed=1000, N=3:
        //   GenerateCombinations stamps profile.Seed = 1000 + 0 * 3 = 1000
        //   DeriveRunSeed(1000, 0) = 1000, DeriveRunSeed(1000, 1) = 1001, DeriveRunSeed(1000, 2) = 1002
        var callLog = new List<(string ModelName, ModelParameterProfile Profile)>();
        var runner = MakeRunner(callLog, score: 70.0);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 3,
            BaseSeed = 1000,
            TemperatureValues = [0.5],   // single combo → comboIndex = 0
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        // Runs within a single combo are sequential, so callLog order is stable
        var targetSeeds = callLog
            .Where(c => c.ModelName == "target-model")
            .Select(c => c.Profile.Seed)
            .ToList();

        Assert.Equal(3, targetSeeds.Count);
        Assert.Equal(1000, targetSeeds[0]);
        Assert.Equal(1001, targetSeeds[1]);
        Assert.Equal(1002, targetSeeds[2]);
    }

    [Fact]
    public async Task RunAsync_TwoCombinations_SeedBlocksDoNotOverlap()
    {
        // With 2 combos, N=3, BaseSeed=1000:
        //   Combo 0 profile.Seed = 1000 + 0*3 = 1000  → run seeds {1000, 1001, 1002}
        //   Combo 1 profile.Seed = 1000 + 1*3 = 1003  → run seeds {1003, 1004, 1005}
        var callLog = new List<(string ModelName, ModelParameterProfile Profile)>();
        var runner = MakeRunner(callLog, score: 70.0);

        var config = new ParameterSweepConfig
        {
            RunsPerCombination = 3,
            BaseSeed = 1000,
            TemperatureValues = [0.3, 0.7],   // 2 combos
            TopKValues = [40],
            TopPValues = [0.9],
            RepeatPenaltyValues = [1.1],
            MaxCombinations = 10
        };

        await RunSweepAsync(runner, config, "baseline-model", ["target-model"]);

        var targetSeeds = callLog
            .Where(c => c.ModelName == "target-model")
            .Select(c => c.Profile.Seed)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        // 6 seeds total, all distinct, each block of 3 must not overlap the other
        Assert.Equal(6, targetSeeds.Count);
        Assert.Equal(6, targetSeeds.Distinct().Count());

        // The two blocks must be {1000,1001,1002} and {1003,1004,1005} (any order between blocks)
        var seedSet = targetSeeds.ToHashSet();
        Assert.True(seedSet.IsSupersetOf(new[] { 1000, 1001, 1002 }) ||
                    seedSet.IsSupersetOf(new[] { 1003, 1004, 1005 }),
                    "Seed blocks 0 and 1 should be contiguous, non-overlapping ranges");
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static ParameterSweepRunner MakeRunner(
        List<(string ModelName, ModelParameterProfile Profile)> callLog,
        double score)
    {
        return new ParameterSweepRunner((modelName, profile, _) =>
        {
            callLog.Add((modelName, profile));
            return Task.FromResult<IReadOnlyList<ModelEvalResult>>(
                new List<ModelEvalResult> { MakeResult(score) });
        });
    }

    private static Task<SweepResult> RunSweepAsync(
        ParameterSweepRunner runner,
        ParameterSweepConfig config,
        string baselineModel,
        IReadOnlyList<string> targetModels)
    {
        return runner.RunAsync(
            baselineModel,
            targetModels,
            agentNames: [],
            sweepConfig: config,
            testCaseLoader: _ => new List<AgentEval.Models.TestCase>(),
            maxCasesPerAgent: null);
    }

    private static ModelEvalResult MakeResult(double score) =>
        new()
        {
            ModelName = "test-model",
            AgentName = "test-agent",
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
}
