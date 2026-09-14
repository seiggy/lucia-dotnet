using lucia.EvalHarness.Providers;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Interactive multi-select prompt for choosing models advertised by a backend.
/// </summary>
public static class ModelSelector
{
    public static IReadOnlyList<string> Select(IReadOnlyList<OllamaModelInfo> models) => Select(models, "Ollama");

    public static IReadOnlyList<string> Select(IReadOnlyList<OllamaModelInfo> models, string backendName)
    {
        if (models.Count == 0)
        {
            AnsiConsole.MarkupLine($"[red]No models found on {Markup.Escape(backendName)}.[/]");
            return [];
        }

        AnsiConsole.MarkupLine($"[bold]Found {models.Count} model(s) on {Markup.Escape(backendName)}[/]");
        AnsiConsole.WriteLine();

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cornflowerblue]Select models to evaluate[/]")
                .Required()
                .PageSize(15)
                .UseConverter(Markup.Escape)
                .InstructionsText("[dim](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoices(models.Select(m => m.Name)));

        AnsiConsole.MarkupLine($"\n[green]\u2713[/] Selected {selected.Count} model(s): {Markup.Escape(string.Join(", ", selected))}");
        AnsiConsole.WriteLine();

        return selected;
    }
}
