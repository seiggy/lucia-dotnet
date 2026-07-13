using Microsoft.Extensions.AI;
using OllamaSharp;
using Spectre.Console;
using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Spectre.Console TUI for running personality evals with LLM-as-Judge scoring,
/// progress display, and rendering the results report.
/// </summary>
public static class PersonalityEvalDisplay
{
    /// <summary>
    /// Runs personality evals for all selected models with a live progress bar,
    /// using a separate judge model for scoring.
    /// </summary>
    public static async Task<IReadOnlyList<PersonalityEvalReport>> RunWithProgressAsync(
        string ollamaEndpoint,
        IReadOnlyList<string> selectedModels,
        IChatClient? judgeChatClient,
        string judgeModelName,
        IReadOnlyList<PersonalityEvalScenario> scenarios,
        IReadOnlyList<PersonalityProfile> selectedProfiles,
        CancellationToken ct = default)
    {
        var runner = new PersonalityEvalRunner();
        var reports = new List<PersonalityEvalReport>();
        var totalCombinations = PersonalityEvalRunner.CountCombinations(scenarios, selectedProfiles);

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
                foreach (var model in selectedModels)
                {
                    var task = ctx.AddTask(
                        $"[bold]{Markup.Escape(model)}[/]",
                        maxValue: totalCombinations);

                    var chatClient = new OllamaApiClient(new Uri(ollamaEndpoint), model);

                    var report = await runner.RunAsync(
                        chatClient,
                        model,
                        judgeChatClient,
                        judgeModelName,
                        scenarios,
                        selectedProfiles,
                        onProgress: _ => task.Increment(1),
                        ct: ct);

                    task.Value = task.MaxValue;
                    reports.Add(report);
                }
            });

        return reports;
    }

    /// <summary>
    /// Renders the full personality eval report to the console.
    /// </summary>
    public static void RenderReport(IReadOnlyList<PersonalityEvalReport> reports)
    {
        foreach (var report in reports)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(
                $"[bold]Personality Eval: {Markup.Escape(report.ModelName)}[/] " +
                $"[dim](judged by {Markup.Escape(report.JudgeModelName)})[/]")
                .LeftJustified());
            AnsiConsole.WriteLine();

            RenderProfileSummaryTable(report);
            RenderCategorySummary(report);
            RenderDetailedResults(report);
            RenderMeaningFailures(report);
        }

        if (reports.Count > 1)
        {
            RenderOverallSummary(reports);
        }
    }

    private static void RenderProfileSummaryTable(PersonalityEvalReport report)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]Profile Summary: {Markup.Escape(report.ModelName)}[/]");

        table.AddColumn(new TableColumn("[bold]Profile[/]").NoWrap());
        table.AddColumn(new TableColumn("[bold]Personality Avg[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Meaning Avg[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Combined Avg[/]").Centered());

        var profileGroups = report.Results
            .GroupBy(r => new { r.ProfileId, r.ProfileName })
            .OrderBy(g => g.Key.ProfileId);

        foreach (var group in profileGroups)
        {
            var personalityAvg = Average(group.Select(result => result.JudgeResult?.PersonalityScore));
            var meaningAvg = Average(group.Select(result => result.JudgeResult?.MeaningScore));
            var combinedAvg = Average(group.Select(result => result.JudgeResult?.CombinedScore));

            table.AddRow(
                Markup.Escape(group.Key.ProfileName),
                ScoreCell(personalityAvg),
                ScoreCell(meaningAvg),
                ScoreCell(combinedAvg));
        }

        // Overall row
        var overallPersonality = report.AveragePersonalityScore;
        var overallMeaning = report.AverageMeaningScore;
        var overallCombined = report.AverageCombinedScore;
        table.AddEmptyRow();
        table.AddRow(
            "[bold]Overall[/]",
            $"[bold]{ScoreCell(overallPersonality)}[/]",
            $"[bold]{ScoreCell(overallMeaning)}[/]",
            $"[bold]{ScoreCell(overallCombined)}[/]");

        AnsiConsole.Write(table);
    }

    private static void RenderCategorySummary(PersonalityEvalReport report)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Category Scores:[/]");

        var categories = report.Results
            .GroupBy(r => r.Category)
            .OrderBy(g => g.Key);

        foreach (var category in categories)
        {
            var pAvg = Average(category.Select(result => result.JudgeResult?.PersonalityScore));
            var mAvg = Average(category.Select(result => result.JudgeResult?.MeaningScore));
            var combined = Average(category.Select(result => result.JudgeResult?.CombinedScore));

            AnsiConsole.MarkupLine(
                $"  {ScoreCell(combined)} {Markup.Escape(category.Key)} " +
                $"[dim](P:{FormatScore(pAvg)} M:{FormatScore(mAvg)})[/]");
        }
    }

    private static void RenderDetailedResults(PersonalityEvalReport report)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Detailed Results[/]").LeftJustified());

        // Show all results with score-based color coding
        var ordered = report.Results.OrderBy(r => r.Score).ToList();
        if (ordered.Count == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]No results.[/]");
            return;
        }

        foreach (var result in ordered)
        {
            AnsiConsole.WriteLine();
            var pScore = result.JudgeResult?.PersonalityScore;
            var mScore = result.JudgeResult?.MeaningScore;
            var combined = result.JudgeResult?.CombinedScore;
            var icon = combined switch
            {
                >= 4 => "[green]\u2714[/]",
                >= 3 => "[yellow]\u25cf[/]",
                null => "[dim]?[/]",
                _ => "[red]\u2718[/]"
            };

            AnsiConsole.MarkupLine(
                $"  {icon} [bold]{Markup.Escape(result.ScenarioId)}[/] \u00d7 " +
                $"[yellow]{Markup.Escape(result.ProfileName)}[/] " +
                $"{ScoreCell(combined)} " +
                $"[dim]({result.DurationMs}ms)[/]");

            if (result.JudgeResult is not null)
            {
                if (combined.HasValue)
                {
                    AnsiConsole.MarkupLine(
                        $"     Personality: {ScoreCell(pScore)} \u2014 {Markup.Escape(result.JudgeResult.PersonalityReason ?? string.Empty)}");
                    AnsiConsole.MarkupLine(
                        $"     Meaning:     {ScoreCell(mScore)} \u2014 {Markup.Escape(result.JudgeResult.MeaningReason ?? string.Empty)}");
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"     [dim]N/A ({Markup.Escape(result.JudgeStatus ?? JudgeAvailability.Unavailable)}): " +
                        $"{Markup.Escape(result.JudgeReason ?? JudgeAvailability.Reason(JudgeAvailability.Unavailable))}[/]");
                }
            }

            if (result.ErrorMessage is not null)
            {
                AnsiConsole.MarkupLine($"     [red]Error:[/] {Markup.Escape(result.ErrorMessage)}");
            }

            if (result.Trace is not null)
            {
                var truncatedResponse = Truncate(result.LlmResponse, 150);
                AnsiConsole.MarkupLine($"     [dim]Response: {Markup.Escape(truncatedResponse)}[/]");
            }
        }
    }

    private static void RenderMeaningFailures(PersonalityEvalReport report)
    {
        var meaningFailures = report.MeaningFailures;
        if (meaningFailures.Count == 0)
            return;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[red bold]\u26a0 Meaning Loss Failures (score < 3)[/]").LeftJustified());
        AnsiConsole.MarkupLine("[red]These scenarios had dangerous meaning loss \u2014 the rewrite changed the intent of the original response.[/]");

        foreach (var result in meaningFailures)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"  [red bold]\u26a0[/] [bold]{Markup.Escape(result.ScenarioId)}[/] \u00d7 " +
                $"[yellow]{Markup.Escape(result.ProfileName)}[/]");

            if (result.JudgeResult is not null)
            {
                AnsiConsole.MarkupLine(
                    $"     Meaning: [red]{result.JudgeResult.MeaningScore}/5[/] \u2014 {Markup.Escape(result.JudgeResult.MeaningReason ?? string.Empty)}");
            }

            if (result.Trace is not null)
            {
                AnsiConsole.MarkupLine($"     [dim]Original:  {Markup.Escape(Truncate(result.Trace.OriginalResponse, 120))}[/]");
                AnsiConsole.MarkupLine($"     [dim]Rewritten: {Markup.Escape(Truncate(result.LlmResponse, 120))}[/]");
            }
        }
    }

    private static void RenderOverallSummary(IReadOnlyList<PersonalityEvalReport> reports)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Overall Model Comparison[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Model[/]")
            .AddColumn(new TableColumn("[bold]Personality[/]").Centered())
            .AddColumn(new TableColumn("[bold]Meaning[/]").Centered())
            .AddColumn(new TableColumn("[bold]Combined[/]").Centered())
            .AddColumn(new TableColumn("[bold]Meaning < 3[/]").Centered())
            .AddColumn(new TableColumn("[bold]Duration[/]").Centered());

        foreach (var report in reports)
        {
            var totalMs = report.Results.Sum(r => r.DurationMs);
            var failColor = report.MeaningFailures.Count > 0 ? "red" : "green";

            table.AddRow(
                Markup.Escape(report.ModelName),
                ScoreCell(report.AveragePersonalityScore),
                ScoreCell(report.AverageMeaningScore),
                ScoreCell(report.AverageCombinedScore),
                $"[{failColor}]{report.MeaningFailures.Count}[/]",
                $"{totalMs / 1000.0:F1}s");
        }

        AnsiConsole.Write(table);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "\u2026";
    }

    private static double? Average(IEnumerable<int?> scores)
    {
        var available = scores.OfType<int>().ToList();
        return available.Count > 0 ? available.Average() : null;
    }

    private static double? Average(IEnumerable<double?> scores)
    {
        var available = scores.OfType<double>().ToList();
        return available.Count > 0 ? available.Average() : null;
    }

    private static string ScoreCell(double? score)
    {
        if (!score.HasValue)
            return "[dim]N/A[/]";

        var color = score.Value >= 4 ? "green" : score.Value >= 3 ? "yellow" : "red";
        return $"[{color}]{score.Value:F1}/5[/]";
    }

    private static string FormatScore(double? score) =>
        score.HasValue ? score.Value.ToString("F1") : "N/A";
}
