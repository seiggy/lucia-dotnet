using Spectre.Console;

namespace lucia.Evals;

/// <summary>
/// Renders evaluation results as rich Spectre.Console output with
/// summary panel, results table, and failure details.
/// </summary>
public sealed class ResultReporter
{
    public void Render(ScenarioRunReport report)
    {
        AnsiConsole.WriteLine();

        RenderSummaryPanel(report);
        RenderResultsTable(report);
        RenderFailureDetails(report);
    }

    private static void RenderSummaryPanel(ScenarioRunReport report)
    {
        var passRate = report.Total > 0
            ? (double)report.Passed / report.Total * 100
            : 0;

        var summaryMarkup = string.Join("\n",
            $"[bold]Total:[/] {report.Total}   " +
            $"[green bold]Passed:[/] {report.Passed}   " +
            $"[red bold]Failed:[/] {report.Failed}   " +
            $"[grey]Skipped:[/] {report.Skipped}",
            $"[bold]Pass Rate:[/] {passRate:F1}%   [bold]Duration:[/] {report.TotalDuration:mm\\:ss\\.fff}");

        var panel = new Panel(new Markup(summaryMarkup))
            .Header("[bold blue] Evaluation Summary [/]")
            .Border(BoxBorder.Rounded)
            .Expand();

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static void RenderResultsTable(ScenarioRunReport report)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Expand()
            .AddColumn(new TableColumn("[bold]Status[/]").Centered().Width(8))
            .AddColumn(new TableColumn("[bold]Scenario[/]"))
            .AddColumn(new TableColumn("[bold]Duration[/]").RightAligned().Width(12))
            .AddColumn(new TableColumn("[bold]Message[/]"));

        var groups = report.Results.GroupBy(r => r.Scenario.Group);

        foreach (var group in groups)
        {
            // Group header row spanning all columns visually
            table.AddRow(
                new Markup(""),
                new Markup($"[bold yellow]── {Markup.Escape(group.Key)} ──[/]"),
                new Markup(""),
                new Markup(""));

            foreach (var (scenario, result) in group)
            {
                var (statusIcon, nameStyle) = result switch
                {
                    { Skipped: true } => ("[grey]⊘[/]", "grey"),
                    { Passed: true } => ("[green]✓[/]", "green"),
                    _ => ("[red]✗[/]", "red"),
                };

                var duration = result.Skipped ? "-" : $"{result.Duration.TotalSeconds:F2}s";
                var message = Markup.Escape(Truncate(result.Message, 60));

                table.AddRow(
                    new Markup(statusIcon),
                    new Markup($"[{nameStyle}]{Markup.Escape(scenario.Name)}[/]"),
                    new Markup($"[grey]{duration}[/]"),
                    new Markup(message));
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void RenderFailureDetails(ScenarioRunReport report)
    {
        var failures = report.Results
            .Where(r => !r.Result.Passed && !r.Result.Skipped)
            .ToList();

        if (failures.Count == 0)
            return;

        AnsiConsole.Write(new Rule("[red bold] Failure Details [/]").LeftJustified());
        AnsiConsole.WriteLine();

        foreach (var (scenario, result) in failures)
        {
            var content = string.IsNullOrWhiteSpace(result.Details)
                ? result.Message
                : $"{result.Message}\n\n{result.Details}";

            var panel = new Panel(new Markup($"[red]{Markup.Escape(Truncate(content, 500))}[/]"))
                .Header($"[red bold] {Markup.Escape(scenario.Name)} [/]")
                .Border(BoxBorder.Heavy)
                .BorderColor(Color.Red);

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
}
