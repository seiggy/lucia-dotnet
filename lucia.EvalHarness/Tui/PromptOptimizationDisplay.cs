using lucia.EvalHarness.Optimization;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Renders prompt optimization results in the Spectre.Console TUI.
/// </summary>
public static class PromptOptimizationDisplay
{
    /// <summary>
    /// Renders a single optimization result with suggestions and the revised prompt.
    /// </summary>
    public static void Render(PromptOptimizationResult result)
    {
        AnsiConsole.Write(new Rule(
            $"[bold cornflowerblue]Prompt Optimization: {Markup.Escape(result.AgentName)} × {Markup.Escape(result.TargetModel)}[/]")
            .LeftJustified());
        AnsiConsole.WriteLine();

        // Score summary
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Current Score", $"[red]{result.CurrentScore:F1}[/]");
        table.AddRow("Baseline Target", $"[green]{result.BaselineScore:F1}[/]");
        table.AddRow("Gap", $"[yellow]{result.BaselineScore - result.CurrentScore:F1}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Analysis
        if (result.Analysis is not null)
        {
            AnsiConsole.Write(new Panel(Markup.Escape(result.Analysis))
                .Header("[bold]Analysis[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.CornflowerBlue));
            AnsiConsole.WriteLine();
        }

        // Suggestions
        if (result.Suggestions.Count > 0)
        {
            AnsiConsole.MarkupLine($"[bold]{result.Suggestions.Count} Suggestions:[/]");
            AnsiConsole.WriteLine();

            foreach (var (suggestion, idx) in result.Suggestions.Select((s, i) => (s, i)))
            {
                var typeColor = suggestion.Type switch
                {
                    "add_example" => "green",
                    "clarify_instruction" => "blue",
                    "add_constraint" => "yellow",
                    "add_tool_guidance" => "cyan",
                    "restructure" => "magenta",
                    _ => "white"
                };

                AnsiConsole.MarkupLine($"  [{typeColor}]{idx + 1}. {Markup.Escape($"[{suggestion.Type}]")}[/] at [dim]{Markup.Escape(suggestion.Location)}[/]");
                AnsiConsole.MarkupLine($"     [dim]Reasoning:[/] {Markup.Escape(Truncate(suggestion.Reasoning, 120))}");
                if (suggestion.PredictedImpact is not null)
                    AnsiConsole.MarkupLine($"     [dim]Impact:[/] [green]{Markup.Escape(suggestion.PredictedImpact)}[/]");
                AnsiConsole.MarkupLine($"     [dim]Content:[/] {Markup.Escape(Truncate(suggestion.Content, 150))}");
                AnsiConsole.WriteLine();
            }
        }

        // Revised prompt preview
        if (result.SuggestedPrompt is not null)
        {
            var preview = result.SuggestedPrompt.Length > 500
                ? result.SuggestedPrompt[..500] + "…"
                : result.SuggestedPrompt;

            AnsiConsole.Write(new Panel(Markup.Escape(preview))
                .Header("[bold green]Suggested Prompt (preview)[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Green));
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Renders multiple optimization results.
    /// </summary>
    public static void RenderAll(IReadOnlyList<PromptOptimizationResult> results)
    {
        AnsiConsole.Write(new Rule("[bold]Prompt Optimization Results[/]").LeftJustified());
        AnsiConsole.WriteLine();

        foreach (var result in results)
        {
            Render(result);
        }
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";
}
