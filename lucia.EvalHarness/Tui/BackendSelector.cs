using lucia.EvalHarness.Configuration;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Interactive multi-select prompt for choosing inference backends to evaluate.
/// </summary>
public static class BackendSelector
{
    /// <summary>
    /// Prompts the user to select one or more backends from the configured list.
    /// When only one backend is configured, auto-selects it without prompting.
    /// </summary>
    public static IReadOnlyList<InferenceBackend> Select(IReadOnlyList<InferenceBackend> backends)
    {
        if (backends.Count <= 1)
            return backends.ToList();

        AnsiConsole.MarkupLine($"[bold]Found {backends.Count} inference backend(s)[/]");
        AnsiConsole.WriteLine();

        var choices = backends.ToDictionary(
            b => $"{b.Name} ({b.Endpoint}) [{b.Type}]",
            b => b);

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cornflowerblue]Select backends to compare[/]")
                .Required()
                .PageSize(10)
                .InstructionsText("[dim](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoices(choices.Keys));

        var result = selected.Select(s => choices[s]).ToList();
        AnsiConsole.MarkupLine($"\n[green]✓[/] Selected {result.Count} backend(s): {Markup.Escape(string.Join(", ", result.Select(b => b.Name)))}");
        AnsiConsole.WriteLine();
        return result;
    }
}
