using Microsoft.Extensions.AI;
using OllamaSharp;
using Spectre.Console;

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
        IChatClient judgeChatClient,
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
        table.AddColumn(new TableColumn("[bold]Pass Rate[/]").Centered());

        var profileGroups = report.Results
            .GroupBy(r => new { r.ProfileId, r.ProfileName })
            .OrderBy(g => g.Key.ProfileId);

        foreach (var group in profileGroups)
        {
            var scored = group.Where(r => r.JudgeResult is not null).ToList();
            var personalityAvg = scored.Count > 0
                ? scored.Average(r => r.JudgeResult!.PersonalityScore)
                : 0.0;
            var meaningAvg = scored.Count > 0
                ? scored.Average(r => r.JudgeResult!.MeaningScore)
                : 0.0;
            var passRate = group.Count() > 0
                ? (double)group.Count(r => r.Passed) / group.Count() * 100
                : 0.0;

            var personalityColor = personalityAvg >= 4 ? "green" : personalityAvg >= 3 ? "yellow" : "red";
            var meaningColor = meaningAvg >= 4 ? "green" : meaningAvg >= 3 ? "yellow" : "red";
            var passColor = passRate >= 80 ? "green" : passRate >= 60 ? "yellow" : "red";

            table.AddRow(
                Markup.Escape(group.Key.ProfileName),
                $"[{personalityColor}]{personalityAvg:F1}/5[/]",
                $"[{meaningColor}]{meaningAvg:F1}/5[/]",
                $"[{passColor}]{passRate:F0}%[/]");
        }

        // Overall row
        var overallPersonality = report.AveragePersonalityScore;
        var overallMeaning = report.AverageMeaningScore;
        var overallPassRate = report.PassRate;
        var opColor = overallPersonality >= 4 ? "green" : overallPersonality >= 3 ? "yellow" : "red";
        var omColor = overallMeaning >= 4 ? "green" : overallMeaning >= 3 ? "yellow" : "red";
        var oprColor = overallPassRate >= 80 ? "green" : overallPassRate >= 60 ? "yellow" : "red";

        table.AddEmptyRow();
        table.AddRow(
            "[bold]Overall[/]",
            $"[bold][{opColor}]{overallPersonality:F1}/5[/][/]",
            $"[bold][{omColor}]{overallMeaning:F1}/5[/][/]",
            $"[bold][{oprColor}]{overallPassRate:F0}%[/][/]");

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
            var scored = category.Where(r => r.JudgeResult is not null).ToList();
            var pAvg = scored.Count > 0 ? scored.Average(r => r.JudgeResult!.PersonalityScore) : 0.0;
            var mAvg = scored.Count > 0 ? scored.Average(r => r.JudgeResult!.MeaningScore) : 0.0;
            var passRate = category.Count() > 0
                ? (double)category.Count(r => r.Passed) / category.Count() * 100
                : 0.0;
            var color = passRate >= 80 ? "green" : passRate >= 60 ? "yellow" : "red";

            AnsiConsole.MarkupLine(
                $"  [{color}]{passRate,5:F0}%[/] {Markup.Escape(category.Key)} " +
                $"[dim](P:{pAvg:F1} M:{mAvg:F1})[/]");
        }
    }

    private static void RenderDetailedResults(PersonalityEvalReport report)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Detailed Results[/]").LeftJustified());

        var failed = report.Results.Where(r => !r.Passed).ToList();
        if (failed.Count == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]All scenarios passed! \ud83c\udf89[/]");
            return;
        }

        foreach (var result in failed)
        {
            AnsiConsole.WriteLine();
            var pScore = result.JudgeResult?.PersonalityScore ?? 0;
            var mScore = result.JudgeResult?.MeaningScore ?? 0;

            AnsiConsole.MarkupLine(
                $"  [red]\u274c[/] [bold]{Markup.Escape(result.ScenarioId)}[/] \u00d7 " +
                $"[yellow]{Markup.Escape(result.ProfileName)}[/] " +
                $"[dim]({result.DurationMs}ms)[/]");

            if (result.JudgeResult is not null)
            {
                var pColor = pScore >= 3 ? "green" : "red";
                var mColor = mScore >= 3 ? "green" : "red";

                AnsiConsole.MarkupLine(
                    $"     Personality: [{pColor}]{pScore}/5[/] \u2014 {Markup.Escape(result.JudgeResult.PersonalityReason)}");
                AnsiConsole.MarkupLine(
                    $"     Meaning:     [{mColor}]{mScore}/5[/] \u2014 {Markup.Escape(result.JudgeResult.MeaningReason)}");
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
                    $"     Meaning: [red]{result.JudgeResult.MeaningScore}/5[/] \u2014 {Markup.Escape(result.JudgeResult.MeaningReason)}");
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
            .AddColumn(new TableColumn("[bold]Pass[/]").Centered())
            .AddColumn(new TableColumn("[bold]Fail[/]").Centered())
            .AddColumn(new TableColumn("[bold]Rate[/]").Centered())
            .AddColumn(new TableColumn("[bold]Duration[/]").Centered());

        foreach (var report in reports)
        {
            var totalMs = report.Results.Sum(r => r.DurationMs);
            var rateColor = report.PassRate >= 80 ? "green" : report.PassRate >= 60 ? "yellow" : "red";
            var pColor = report.AveragePersonalityScore >= 4 ? "green" : report.AveragePersonalityScore >= 3 ? "yellow" : "red";
            var mColor = report.AverageMeaningScore >= 4 ? "green" : report.AverageMeaningScore >= 3 ? "yellow" : "red";

            table.AddRow(
                Markup.Escape(report.ModelName),
                $"[{pColor}]{report.AveragePersonalityScore:F1}/5[/]",
                $"[{mColor}]{report.AverageMeaningScore:F1}/5[/]",
                $"[green]{report.PassCount}[/]",
                $"[red]{report.FailCount}[/]",
                $"[{rateColor}]{report.PassRate:F1}%[/]",
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
}
