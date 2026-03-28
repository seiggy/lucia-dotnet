using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Providers;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Displays live evaluation progress using Spectre.Console progress bars.
/// </summary>
public static class EvalProgressDisplay
{
    /// <summary>
    /// Runs evaluations with a live progress display showing per-model, per-agent progress.
    /// When multiple parameter profiles are provided, each model is evaluated once per profile.
    /// Results use a composite display name (<c>model@profile</c>) so reports can compare profiles.
    /// </summary>
    public static async Task<EvalRunResult> RunWithProgressAsync(
        EvalRunner runner,
        RealAgentFactory agentFactory,
        IReadOnlyList<string> selectedModels,
        IReadOnlyList<string> selectedAgentNames,
        Func<string, IReadOnlyList<AgentEval.Models.TestCase>> testCaseLoader,
        int? maxCasesPerAgent,
        IReadOnlyList<ModelParameterProfile>? parameterProfiles = null,
        CancellationToken ct = default)
    {
        var profiles = parameterProfiles is { Count: > 0 }
            ? parameterProfiles
            : [ModelParameterProfile.Default];
        var multiProfile = profiles.Count > 1;

        var agentResults = new List<AgentEvalResult>();
        var startedAt = DateTimeOffset.UtcNow;
        var factories = agentFactory.AgentFactories;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                foreach (var agentName in selectedAgentNames)
                {
                    if (!factories.TryGetValue(agentName, out var createAgent))
                        continue;

                    var totalTasks = selectedModels.Count * profiles.Count;
                    var agentTask = ctx.AddTask(
                        $"[bold]{Markup.Escape(agentName)}[/]",
                        maxValue: totalTasks);

                    var modelResults = new List<ModelEvalResult>();

                    foreach (var profile in profiles)
                    {
                        // Apply this profile to the factory for agent construction
                        agentFactory.ParameterProfile = profile;

                        foreach (var model in selectedModels)
                        {
                            var displayLabel = multiProfile
                                ? $"  {Markup.Escape(model)} @ {Markup.Escape(profile.Name)}"
                                : $"  {Markup.Escape(model)}";
                            var modelTask = ctx.AddTask(displayLabel, maxValue: 1);

                            AnsiConsole.MarkupLine(
                                $"[dim]  Constructing {Markup.Escape(agentName)} with {Markup.Escape(model)}" +
                                (multiProfile ? $" ({Markup.Escape(profile.Name)})" : "") + "...[/]");

                            var agentInstance = await createAgent(model);

                            var scenarios = ScenarioLoader.LoadFromFile(agentInstance.DatasetFile);

                            ModelEvalResult result;
                            if (scenarios.Count > 0)
                            {
                                var scenarioList = maxCasesPerAgent.HasValue
                                    ? scenarios.Take(maxCasesPerAgent.Value).ToList()
                                    : scenarios.ToList();

                                modelTask.MaxValue = scenarioList.Count;

                                result = await runner.EvaluateScenariosAsync(
                                    model,
                                    agentInstance,
                                    scenarioList,
                                    agentFactory.HomeAssistantClient,
                                    parameterProfile: profile,
                                    onProgress: _ => modelTask.Increment(1),
                                    ct: ct);
                            }
                            else
                            {
                                var allCases = testCaseLoader(agentInstance.DatasetFile);
                                var cases = maxCasesPerAgent.HasValue
                                    ? allCases.Take(maxCasesPerAgent.Value).ToList()
                                    : allCases.ToList();

                                modelTask.MaxValue = cases.Count;

                                result = await runner.EvaluateRealAgentAsync(
                                    model,
                                    agentInstance,
                                    cases,
                                    parameterProfile: profile,
                                    onProgress: _ => modelTask.Increment(1),
                                    ct: ct);
                            }

                            modelTask.Value = modelTask.MaxValue;
                            agentTask.Increment(1);
                            modelResults.Add(result);
                        }
                    }

                    agentTask.Value = totalTasks;
                    agentResults.Add(new AgentEvalResult
                    {
                        AgentName = agentName,
                        ModelResults = modelResults
                    });
                }
            });

        return new EvalRunResult
        {
            RunId = Guid.NewGuid().ToString("N")[..8],
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            AgentResults = agentResults
        };
    }
}
