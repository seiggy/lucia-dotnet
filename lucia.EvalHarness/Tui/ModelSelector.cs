using lucia.EvalHarness.Providers;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Interactive multi-select prompt for choosing Ollama models to evaluate.
/// </summary>
public static class ModelSelector
{
    public static IReadOnlyList<string> Select(IReadOnlyList<OllamaModelInfo> models)
    {
        if (models.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No models found on Ollama.[/]");
            return [];
        }

        AnsiConsole.MarkupLine($"[bold]Found {models.Count} model(s) on Ollama[/]");
        AnsiConsole.WriteLine();

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cornflowerblue]Select models to evaluate[/]")
                .Required()
                .PageSize(15)
                .InstructionsText("[dim](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoices(models.Select(m => m.Name)));

        AnsiConsole.MarkupLine($"\n[green]\u2713[/] Selected {selected.Count} model(s): {Markup.Escape(string.Join(", ", selected))}");
        AnsiConsole.WriteLine();

        return selected;
    }
}
