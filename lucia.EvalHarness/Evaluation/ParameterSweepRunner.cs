using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using Spectre.Console;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Result of a parameter sweep experiment for a single model across
/// multiple parameter configurations, compared against a baseline.
/// </summary>
public sealed class SweepResult
{
    public required string RunId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// The baseline model's evaluation results (benchmark target).
    /// </summary>
    public required IReadOnlyList<ModelEvalResult> BaselineResults { get; init; }

    /// <summary>
    /// Results per target model, keyed by model name.
    /// Each entry maps parameter profile name → evaluation results.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<SweepEntry>> TargetResults { get; init; }
}

/// <summary>
/// A single sweep entry: one model evaluated with a specific parameter profile.
/// </summary>
public sealed class SweepEntry
{
    public required ModelParameterProfile Profile { get; init; }
    public required IReadOnlyList<ModelEvalResult> Results { get; init; }

    /// <summary>
    /// Average overall score across all agents for this profile.
    /// </summary>
    public double AverageScore => Results.Count > 0
        ? Results.Average(r => r.OverallScore)
        : 0;

    /// <summary>
    /// Average latency in milliseconds across all agents.
    /// </summary>
    public double AverageLatencyMs => Results.Count > 0
        ? Results.Average(r => r.Performance.MeanLatency.TotalMilliseconds)
        : 0;
}

/// <summary>
/// Orchestrates parameter sweep experiments: runs each target model at multiple
/// parameter configurations and compares against a baseline model's scores.
/// </summary>
public sealed class ParameterSweepRunner
{
    private readonly EvalRunner _evalRunner;
    private readonly RealAgentFactory _agentFactory;

    public ParameterSweepRunner(EvalRunner evalRunner, RealAgentFactory agentFactory)
    {
        _evalRunner = evalRunner;
        _agentFactory = agentFactory;
    }

    /// <summary>
    /// Runs a baseline evaluation with the best/largest model, then sweeps
    /// parameter configurations for each target model.
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

        // 1. Run baseline with default parameters
        AnsiConsole.MarkupLine($"[bold]Running baseline:[/] {Markup.Escape(baselineModel)} (default parameters)");

        _agentFactory.ParameterProfile = ModelParameterProfile.Default;
        var baselineResults = await RunModelAcrossAgentsAsync(
            baselineModel, agentNames, testCaseLoader, maxCasesPerAgent, ModelParameterProfile.Default, ct);

        var baselineAvg = baselineResults.Average(r => r.OverallScore);
        AnsiConsole.MarkupLine($"[green]\u2713[/] Baseline score: {baselineAvg:F1}");
        AnsiConsole.WriteLine();

        // 2. Sweep each target model
        var targetResults = new Dictionary<string, IReadOnlyList<SweepEntry>>();

        foreach (var targetModel in targetModels)
        {
            AnsiConsole.MarkupLine($"[bold]Sweeping:[/] {Markup.Escape(targetModel)} ({combinations.Count} parameter configurations)");

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
                        _agentFactory.ParameterProfile = profile;

                        var results = await RunModelAcrossAgentsAsync(
                            targetModel, agentNames, testCaseLoader, maxCasesPerAgent, profile, ct);

                        entries.Add(new SweepEntry
                        {
                            Profile = profile,
                            Results = results
                        });

                        progressTask.Increment(1);
                    }
                });

            var bestEntry = entries.OrderByDescending(e => e.AverageScore).First();
            AnsiConsole.MarkupLine(
                $"[green]\u2713[/] Best config: {Markup.Escape(bestEntry.Profile.ToSummary())} → {bestEntry.AverageScore:F1} " +
                $"(delta from baseline: {bestEntry.AverageScore - baselineAvg:+0.0;-0.0})");
            AnsiConsole.WriteLine();

            targetResults[targetModel] = entries;
        }

        return new SweepResult
        {
            RunId = Guid.NewGuid().ToString("N")[..8],
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            BaselineResults = baselineResults,
            TargetResults = targetResults
        };
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
