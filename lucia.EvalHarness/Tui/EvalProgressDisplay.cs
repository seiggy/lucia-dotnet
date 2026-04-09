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
    /// When multiple backends are provided, each model is evaluated once per backend and the
    /// result model name is tagged as <c>model@backend</c> for comparison reporting.
    /// </summary>
    public static async Task<EvalRunResult> RunWithProgressAsync(
        EvalRunner runner,
        IReadOnlyList<(InferenceBackend Backend, RealAgentFactory Factory)> backendFactories,
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
        var multiBackend = backendFactories.Count > 1;

        var agentResults = new List<AgentEvalResult>();
        var startedAt = DateTimeOffset.UtcNow;

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
                    var totalTasks = selectedModels.Count * profiles.Count * backendFactories.Count;
                    var agentTask = ctx.AddTask(
                        $"[bold]{Markup.Escape(agentName)}[/]",
                        maxValue: totalTasks);

                    var modelResults = new List<ModelEvalResult>();

                    foreach (var (backend, agentFactory) in backendFactories)
                    {
                        if (!agentFactory.AgentFactories.TryGetValue(agentName, out var createAgent))
                            continue;

                        foreach (var profile in profiles)
                        {
                            agentFactory.ParameterProfile = profile;

                            foreach (var model in selectedModels)
                            {
                                // Tag model name with backend when comparing multiple backends
                                var displayModel = multiBackend ? $"{model}@{backend.Name}" : model;

                                var displayLabel = multiProfile
                                    ? $"  {Markup.Escape(displayModel)} @ {Markup.Escape(profile.Name)}"
                                    : $"  {Markup.Escape(displayModel)}";
                                var modelTask = ctx.AddTask(displayLabel, maxValue: 1);

                                var backendSuffix = multiBackend ? $" ({Markup.Escape(backend.Name)})" : "";
                                AnsiConsole.MarkupLine(
                                    $"[dim]  Constructing {Markup.Escape(agentName)} with {Markup.Escape(model)}" +
                                    (multiProfile ? $" ({Markup.Escape(profile.Name)})" : "") +
                                    backendSuffix + "...[/]");

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
                                        displayModel,
                                        agentInstance,
                                        scenarioList,
                                        agentFactory.HomeAssistantClient,
                                        agentFactory.EntityLocationService,
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
                                        displayModel,
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

    /// <summary>
    /// Single-backend overload for backward compatibility.
    /// </summary>
    public static Task<EvalRunResult> RunWithProgressAsync(
        EvalRunner runner,
        RealAgentFactory agentFactory,
        IReadOnlyList<string> selectedModels,
        IReadOnlyList<string> selectedAgentNames,
        Func<string, IReadOnlyList<AgentEval.Models.TestCase>> testCaseLoader,
        int? maxCasesPerAgent,
        IReadOnlyList<ModelParameterProfile>? parameterProfiles = null,
        CancellationToken ct = default)
    {
        var backendFactories = new List<(InferenceBackend, RealAgentFactory)>
        {
            (agentFactory.Backend, agentFactory)
        };
        return RunWithProgressAsync(
            runner, backendFactories, selectedModels, selectedAgentNames,
            testCaseLoader, maxCasesPerAgent, parameterProfiles, ct);
    }
}
