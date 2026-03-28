using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Prompts the user to select the evaluation type: standard agent eval or personality eval.
/// </summary>
public static class EvalTypeSelector
{
    public const string StandardAgentEval = "Standard Agent Eval";
    public const string PersonalityEval = "Personality Eval";

    public static string Select()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cornflowerblue]Select evaluation type[/]")
                .AddChoices(StandardAgentEval, PersonalityEval));

        AnsiConsole.MarkupLine($"[green]\u2713[/] {Markup.Escape(choice)}");
        AnsiConsole.WriteLine();

        return choice;
    }
}
