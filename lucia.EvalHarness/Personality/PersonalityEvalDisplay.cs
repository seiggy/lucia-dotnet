using OllamaSharp;
using Spectre.Console;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Spectre.Console TUI for running personality evals with progress display
/// and rendering the results report.
/// </summary>
public static class PersonalityEvalDisplay
{
    /// <summary>
    /// Runs personality evals for all selected models with a live progress bar,
    /// then renders the full report.
    /// </summary>
    public static async Task<IReadOnlyList<PersonalityEvalReport>> RunWithProgressAsync(
        string ollamaEndpoint,
        IReadOnlyList<string> selectedModels,
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
            AnsiConsole.Write(new Rule($"[bold]Personality Eval: {Markup.Escape(report.ModelName)}[/]").LeftJustified());
            AnsiConsole.WriteLine();

            RenderResultsTable(report);
            RenderCategorySummary(report);
            RenderProfileSummary(report);
            RenderFailedDetails(report);
        }

        if (reports.Count > 1)
        {
            RenderOverallSummary(reports);
        }
    }

    private static void RenderResultsTable(PersonalityEvalReport report)
    {
        var profileIds = report.ProfileIds;
        var scenarioIds = report.ScenarioIds;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title($"[bold]Results: {Markup.Escape(report.ModelName)}[/]");

        table.AddColumn(new TableColumn("[bold]Scenario[/]").NoWrap());
        foreach (var profileId in profileIds)
        {
            var profileName = report.Results
                .First(r => r.ProfileId == profileId).ProfileName;
            table.AddColumn(new TableColumn($"[bold]{Markup.Escape(profileName)}[/]").Centered());
        }

        foreach (var scenarioId in scenarioIds)
        {
            var scenarioDesc = report.Results
                .First(r => r.ScenarioId == scenarioId).ScenarioDescription;

            var cells = new List<string> { Markup.Escape(Truncate(scenarioDesc, 45)) };

            foreach (var profileId in profileIds)
            {
                var result = report.Results
                    .FirstOrDefault(r => r.ScenarioId == scenarioId && r.ProfileId == profileId);

                if (result is null)
                    cells.Add("[dim]\u2014[/]");
                else if (result.Passed)
                    cells.Add("[green]\u2705[/]");
                else
                    cells.Add("[red]\u274c[/]");
            }

            table.AddRow(cells.ToArray());
        }

        // Pass rate footer row
        var rateRow = new List<string> { "[bold]Pass Rate[/]" };
        foreach (var profileId in profileIds)
        {
            var profileResults = report.Results.Where(r => r.ProfileId == profileId).ToList();
            var rate = profileResults.Count > 0
                ? (double)profileResults.Count(r => r.Passed) / profileResults.Count * 100
                : 0;
            var color = rate >= 80 ? "green" : rate >= 60 ? "yellow" : "red";
            rateRow.Add($"[{color}]{rate:F0}%[/]");
        }
        table.AddRow(rateRow.ToArray());

        AnsiConsole.Write(table);
    }

    private static void RenderCategorySummary(PersonalityEvalReport report)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Category Pass Rates:[/]");

        var categories = report.Results
            .GroupBy(r => r.Category)
            .OrderBy(g => g.Key);

        foreach (var category in categories)
        {
            var total = category.Count();
            var passed = category.Count(r => r.Passed);
            var rate = (double)passed / total * 100;
            var color = rate >= 80 ? "green" : rate >= 60 ? "yellow" : "red";
            AnsiConsole.MarkupLine(
                $"  [{color}]{rate,5:F0}%[/] {Markup.Escape(category.Key)} ({passed}/{total})");
        }
    }

    private static void RenderProfileSummary(PersonalityEvalReport report)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Profile Summary:[/]");

        var profiles = report.Results
            .GroupBy(r => new { r.ProfileId, r.ProfileName })
            .OrderBy(g => g.Key.ProfileId);

        foreach (var profile in profiles)
        {
            var total = profile.Count();
            var passed = profile.Count(r => r.Passed);
            var rate = (double)passed / total * 100;
            var color = rate >= 80 ? "green" : rate >= 60 ? "yellow" : "red";
            AnsiConsole.MarkupLine(
                $"  [{color}]{rate,5:F0}%[/] {Markup.Escape(profile.Key.ProfileName)} ({passed}/{total})");
        }

        var overallColor = report.PassRate >= 80 ? "green" : report.PassRate >= 60 ? "yellow" : "red";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[bold]Overall:[/] {report.PassCount}/{report.TotalCombinations} passed " +
            $"([{overallColor}]{report.PassRate:F1}%[/])");
    }

    private static void RenderFailedDetails(PersonalityEvalReport report)
    {
        var failed = report.Results.Where(r => !r.Passed).ToList();
        if (failed.Count == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]All scenarios passed! \ud83c\udf89[/]");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[red]Failed Scenarios[/]").LeftJustified());

        foreach (var result in failed)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(
                $"  [red]\u274c[/] [bold]{Markup.Escape(result.ScenarioId)}[/] \u00d7 " +
                $"[yellow]{Markup.Escape(result.ProfileName)}[/] " +
                $"[dim]({result.DurationMs}ms)[/]");

            foreach (var check in result.FailedChecks)
            {
                AnsiConsole.MarkupLine($"     [red]\u2022[/] {Markup.Escape(check)}");
            }

            var truncatedResponse = Truncate(result.LlmResponse, 200);
            AnsiConsole.MarkupLine($"     [dim]Response: {Markup.Escape(truncatedResponse)}[/]");
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
            .AddColumn(new TableColumn("[bold]Pass[/]").Centered())
            .AddColumn(new TableColumn("[bold]Fail[/]").Centered())
            .AddColumn(new TableColumn("[bold]Rate[/]").Centered())
            .AddColumn(new TableColumn("[bold]Duration[/]").Centered());

        foreach (var report in reports)
        {
            var totalMs = report.Results.Sum(r => r.DurationMs);
            var rateColor = report.PassRate >= 80 ? "green" : report.PassRate >= 60 ? "yellow" : "red";

            table.AddRow(
                Markup.Escape(report.ModelName),
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
