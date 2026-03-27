using Spectre.Console;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Interactive multi-select prompt for choosing which personality profiles to evaluate.
/// All profiles are pre-selected by default.
/// </summary>
public static class PersonalityProfileSelector
{
    public static IReadOnlyList<PersonalityProfile> Select(
        IReadOnlyList<PersonalityProfile> allProfiles)
    {
        if (allProfiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No personality profiles found.[/]");
            return [];
        }

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cornflowerblue]Select personality profiles to evaluate[/]")
                .Required()
                .PageSize(10)
                .InstructionsText("[dim](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoiceGroup("All Profiles", allProfiles.Select(p => $"{p.Name} ({p.Id})")));

        var selectedProfiles = allProfiles
            .Where(p => selected.Any(s => s.Contains(p.Id)))
            .ToList();

        AnsiConsole.MarkupLine(
            $"\n[green]\u2713[/] Selected {selectedProfiles.Count} profile(s): " +
            Markup.Escape(string.Join(", ", selectedProfiles.Select(p => p.Name))));
        AnsiConsole.WriteLine();

        return selectedProfiles;
    }
}
