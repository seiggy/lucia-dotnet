using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using Spectre.Console;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Orchestrates parameter sweep experiments: runs each target model at multiple
/// parameter configurations and compares against a baseline model's scores.
/// </summary>
public sealed class ParameterSweepRunner
{
    private readonly EvalRunner _evalRunner;
    private readonly RealAgentFactory _agentFactory;

    // Injectable evaluation backend for testing. When non-null, replaces the
    // real agent factory / eval runner calls for both baseline and sweep runs.
    private readonly Func<string, ModelParameterProfile, CancellationToken,
        Task<IReadOnlyList<ModelEvalResult>>>? _evalBackend;

    public ParameterSweepRunner(EvalRunner evalRunner, RealAgentFactory agentFactory)
    {
        _evalRunner = evalRunner;
        _agentFactory = agentFactory;
    }

    /// <summary>
    /// Creates a runner with a custom evaluation backend, intended for testing or
    /// embedding the sweep runner in scenarios where the full agent factory is
    /// unavailable. The delegate is called once per (modelName, profile, run).
    /// </summary>
    public ParameterSweepRunner(
        Func<string, ModelParameterProfile, CancellationToken, Task<IReadOnlyList<ModelEvalResult>>> evalBackend)
    {
        _evalRunner = null!;
        _agentFactory = null!;
        _evalBackend = evalBackend;
    }

    /// <summary>
    /// Runs a baseline evaluation with the best/largest model, then sweeps
    /// parameter configurations for each target model.
    /// Both baseline and each combination are evaluated RunsPerCombination times;
    /// the winner is chosen by mean score across all runs, with variance as a
    /// tie-breaker. Deltas are mean-to-mean to avoid single-run baseline noise.
    /// </summary>
    public async Task<SweepResult> RunAsync(
        string baselineModel,
        IReadOnlyList<string> targetModels,
        IReadOnlyList<string> agentNames,
        ParameterSweepConfig sweepConfig,
        Func<string, IReadOnlyList<AgentEval.Models.TestCase>> testCaseLoader,
        int? maxCasesPerAgent,
        CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var combinations = sweepConfig.GenerateCombinations();
        var runsPerCombination = Math.Max(1, sweepConfig.RunsPerCombination);

        // 1. Run baseline N times so baseline noise matches sweep noise
        AnsiConsole.MarkupLine(
            $"[bold]Running baseline:[/] {Markup.Escape(baselineModel)} " +
            $"(default parameters, {runsPerCombination} run{(runsPerCombination == 1 ? "" : "s")})");

        var baselineAllRuns = new List<IReadOnlyList<ModelEvalResult>>(runsPerCombination);
        for (var runIndex = 0; runIndex < runsPerCombination; runIndex++)
        {
            // Derive a seed for each baseline run when BaseSeed is configured
            var baselineRunProfile = sweepConfig.BaseSeed.HasValue
                ? ModelParameterProfile.Default with
                  { Seed = SweepRunAggregator.DeriveRunSeed(sweepConfig.BaseSeed, runIndex) }
                : ModelParameterProfile.Default;

            var run = await EvalAsync(baselineModel, agentNames, testCaseLoader,
                maxCasesPerAgent, baselineRunProfile, ct);
            baselineAllRuns.Add(run);
        }

        var baselineResults = baselineAllRuns[0]; // first run for per-agent display
        var baselineMeanScore = SweepRunAggregator.ComputeMean(baselineAllRuns);
        AnsiConsole.MarkupLine(
            $"[green]\u2713[/] Baseline mean score: {FormatScore(baselineMeanScore)}");
        AnsiConsole.WriteLine();

        // 2. Sweep each target model
        var targetResults = new Dictionary<string, IReadOnlyList<SweepEntry>>();

        foreach (var targetModel in targetModels)
        {
            AnsiConsole.MarkupLine(
                $"[bold]Sweeping:[/] {Markup.Escape(targetModel)} " +
                $"({combinations.Count} configurations x {runsPerCombination} run{(runsPerCombination == 1 ? "" : "s")} each)");

            var entries = new List<SweepEntry>();

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var progressTask = ctx.AddTask(
                        $"[dim]{Markup.Escape(targetModel)}[/]",
                        maxValue: combinations.Count);

                    foreach (var profile in combinations)
                    {
                        var allRunResults = new List<IReadOnlyList<ModelEvalResult>>(runsPerCombination);

                        for (var runIndex = 0; runIndex < runsPerCombination; runIndex++)
                        {
                            // profile.Seed is already set by GenerateCombinations() when BaseSeed
                            // is configured; DeriveRunSeed offsets it by runIndex so each run gets
                            // a unique, reproducible seed within the combination's allocated block.
                            var runProfile = profile.Seed.HasValue
                                ? profile with { Seed = SweepRunAggregator.DeriveRunSeed(profile.Seed, runIndex) }
                                : profile;

                            if (_evalBackend is null)
                                _agentFactory.ParameterProfile = runProfile;

                            var runResults = await EvalAsync(
                                targetModel, agentNames, testCaseLoader, maxCasesPerAgent, runProfile, ct);

                            allRunResults.Add(runResults);
                        }

                        entries.Add(new SweepEntry
                        {
                            Profile = profile,
                            Results = allRunResults[0],  // first run -- used for per-agent display
                            AllRunResults = allRunResults
                        });

                        progressTask.Increment(1);
                    }
                });

            var bestEntry = SweepRunAggregator.SelectWinner(entries);
            if (bestEntry is null)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]![/] No winning config: every score was unavailable.");
            }
            else
            {
                var delta = bestEntry.MeanScore.HasValue && baselineMeanScore.HasValue
                    ? $"{bestEntry.MeanScore.Value - baselineMeanScore.Value:+0.0;-0.0}"
                    : "N/A";
                AnsiConsole.MarkupLine(
                    $"[green]\u2713[/] Best config: {Markup.Escape(bestEntry.Profile.ToSummary())} -> " +
                    $"{FormatScore(bestEntry.MeanScore)} (σ={bestEntry.ScoreStdDev:F2}, " +
                    $"{runsPerCombination}-run mean, delta from baseline: {delta})");
            }
            AnsiConsole.WriteLine();

            targetResults[targetModel] = entries;
        }

        return new SweepResult
        {
            RunId = Guid.NewGuid().ToString("N")[..8],
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            BaselineResults = baselineResults,
            BaselineMeanScore = baselineMeanScore,
            TargetResults = targetResults
        };
    }

    private static string FormatScore(double? score) =>
        score.HasValue ? score.Value.ToString("F1") : "N/A";

    // Dispatches to either the injectable backend (tests) or the real agent factory + eval runner.
    private async Task<IReadOnlyList<ModelEvalResult>> EvalAsync(
        string modelName,
        IReadOnlyList<string> agentNames,
        Func<string, IReadOnlyList<AgentEval.Models.TestCase>> testCaseLoader,
        int? maxCasesPerAgent,
        ModelParameterProfile profile,
        CancellationToken ct)
    {
        if (_evalBackend is not null)
            return await _evalBackend(modelName, profile, ct);

        return await RunModelAcrossAgentsAsync(modelName, agentNames, testCaseLoader, maxCasesPerAgent, profile, ct);
    }

    private async Task<IReadOnlyList<ModelEvalResult>> RunModelAcrossAgentsAsync(
        string modelName,
        IReadOnlyList<string> agentNames,
        Func<string, IReadOnlyList<AgentEval.Models.TestCase>> testCaseLoader,
        int? maxCasesPerAgent,
        ModelParameterProfile profile,
        CancellationToken ct)
    {
        var results = new List<ModelEvalResult>();
        var factories = _agentFactory.AgentFactories;

        foreach (var agentName in agentNames)
        {
            if (!factories.TryGetValue(agentName, out var createAgent))
                continue;

            var agentInstance = await createAgent(modelName);
            var scenarios = ScenarioLoader.LoadFromFile(agentInstance.DatasetFile);

            ModelEvalResult result;
            if (scenarios.Count > 0)
            {
                var scenarioList = maxCasesPerAgent.HasValue
                    ? scenarios.Take(maxCasesPerAgent.Value).ToList()
                    : scenarios.ToList();

                result = await _evalRunner.EvaluateScenariosAsync(
                    modelName, agentInstance, scenarioList,
                    _agentFactory.HomeAssistantClient,
                    _agentFactory.EntityLocationService,
                    parameterProfile: profile, ct: ct);
            }
            else
            {
                var allCases = testCaseLoader(agentInstance.DatasetFile);
                var cases = maxCasesPerAgent.HasValue
                    ? allCases.Take(maxCasesPerAgent.Value).ToList()
                    : allCases.ToList();

                result = await _evalRunner.EvaluateRealAgentAsync(
                    modelName, agentInstance, cases,
                    parameterProfile: profile, ct: ct);
            }

            results.Add(result);
        }

        return results;
    }
}
