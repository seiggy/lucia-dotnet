using System.Diagnostics;
using Spectre.Console;

namespace lucia.Evals;

/// <summary>
/// Executes evaluation scenarios with a live Spectre.Console progress display.
/// </summary>
public sealed class ScenarioRunner
{
    /// <summary>
    /// Runs the given scenarios sequentially, displaying live progress.
    /// </summary>
    public async Task<ScenarioRunReport> RunAsync(IReadOnlyList<EvalScenario> scenarios)
    {
        var results = new List<(EvalScenario Scenario, EvalScenarioResult Result)>();
        var overallSw = Stopwatch.StartNew();

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new SpinnerColumn(),
                new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var taskMap = new Dictionary<string, ProgressTask>();
                foreach (var scenario in scenarios)
                {
                    var task = ctx.AddTask($"[grey]{Markup.Escape(scenario.Name)}[/]", maxValue: 1.0);
                    task.IsIndeterminate = true;
                    taskMap[scenario.Name] = task;
                }

                foreach (var scenario in scenarios)
                {
                    var progressTask = taskMap[scenario.Name];
                    progressTask.IsIndeterminate = false;
                    progressTask.Description = $"[yellow]{Markup.Escape(scenario.Name)}[/]";

                    EvalScenarioResult result;
                    try
                    {
                        result = await scenario.RunAsync();
                    }
                    catch (Exception ex)
                    {
                        result = EvalScenarioResult.Fail(TimeSpan.Zero, ex.Message, ex.ToString());
                    }

                    results.Add((scenario, result));

                    if (result.Skipped)
                        progressTask.Description = $"[grey]\u229c {Markup.Escape(scenario.Name)}[/]";
                    else if (result.Passed)
                        progressTask.Description = $"[green]\u2713 {Markup.Escape(scenario.Name)}[/]";
                    else
                        progressTask.Description = $"[red]\u2717 {Markup.Escape(scenario.Name)}[/]";

                    progressTask.Value = 1.0;
                    progressTask.StopTask();
                }
            });

        overallSw.Stop();

        return new ScenarioRunReport
        {
            Results = results,
            TotalDuration = overallSw.Elapsed
        };
    }
}
