using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Interactive multi-select prompt for choosing which lucia agents to evaluate.
/// </summary>
public static class AgentSelector
{
    public static IReadOnlyList<string> Select(IReadOnlyList<string> availableAgentNames)
    {
        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cornflowerblue]Select agents to evaluate[/]")
                .Required()
                .PageSize(10)
                .InstructionsText("[dim](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoiceGroup("All Agents", availableAgentNames));

        AnsiConsole.MarkupLine($"\n[green]\u2713[/] Selected {selected.Count} agent(s): {string.Join(", ", selected)}");
        AnsiConsole.WriteLine();

        return selected;
    }
}
