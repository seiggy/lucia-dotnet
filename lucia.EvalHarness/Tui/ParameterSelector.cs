using lucia.EvalHarness.Configuration;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Interactive TUI for selecting model inference parameter profiles.
/// Supports both single-select and multi-select modes for profile comparison.
/// </summary>
public static class ParameterSelector
{
    /// <summary>
    /// Prompts the user to select one or more parameter profiles.
    /// When multiple profiles are selected, the harness runs evaluations
    /// for each profile and produces a comparison report.
    /// </summary>
    public static IReadOnlyList<ModelParameterProfile> SelectMultiple(
        IReadOnlyDictionary<string, ModelParameterProfile> profiles)
    {
        var compareMode = AnsiConsole.Confirm(
            "[cornflowerblue]Compare multiple parameter profiles?[/] " +
            "(runs evals per profile and shows variance)",
            defaultValue: false);

        if (!compareMode)
        {
            return [Select(profiles)];
        }

        var choices = profiles
            .Select(p => $"{p.Key} ({p.Value.ToSummary()})")
            .ToList();

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cornflowerblue]Select profiles to compare:[/]")
                .PageSize(10)
                .InstructionsText("[dim](Space to toggle, Enter to confirm)[/]")
                .Required()
                .AddChoices(choices));

        return selected
            .Select(s => profiles[s.Split(' ')[0]])
            .ToList();
    }

    /// <summary>
    /// Prompts the user to select a single parameter profile.
    /// </summary>
    public static ModelParameterProfile Select(IReadOnlyDictionary<string, ModelParameterProfile> profiles)
    {
        var choices = profiles
            .Select(p => $"{p.Key} ({p.Value.ToSummary()})")
            .Append("Custom...")
            .ToList();

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cornflowerblue]Select parameter profile:[/]")
                .PageSize(10)
                .AddChoices(choices));

        if (selection == "Custom...")
            return PromptCustomProfile();

        var profileName = selection.Split(' ')[0];
        return profiles[profileName];
    }

    private static ModelParameterProfile PromptCustomProfile()
    {
        AnsiConsole.MarkupLine("[dim]Configure custom inference parameters:[/]");

        var temperature = AnsiConsole.Prompt(
            new TextPrompt<double>("  [cornflowerblue]Temperature[/] (0.0\u20132.0):")
                .DefaultValue(0.8)
                .Validate(v => v is >= 0 and <= 2
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Must be between 0.0 and 2.0")));

        var topK = AnsiConsole.Prompt(
            new TextPrompt<int>("  [cornflowerblue]Top-K[/] (1\u2013200):")
                .DefaultValue(40)
                .Validate(v => v is >= 1 and <= 200
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Must be between 1 and 200")));

        var topP = AnsiConsole.Prompt(
            new TextPrompt<double>("  [cornflowerblue]Top-P[/] (0.0\u20131.0):")
                .DefaultValue(0.9)
                .Validate(v => v is >= 0 and <= 1
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Must be between 0.0 and 1.0")));

        var repeatPenalty = AnsiConsole.Prompt(
            new TextPrompt<double>("  [cornflowerblue]Repeat Penalty[/] (0.0\u20132.0):")
                .DefaultValue(1.1)
                .Validate(v => v is >= 0 and <= 2
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Must be between 0.0 and 2.0")));

        AnsiConsole.WriteLine();

        return new ModelParameterProfile
        {
            Name = "custom",
            Temperature = temperature,
            TopK = topK,
            TopP = topP,
            RepeatPenalty = repeatPenalty
        };
    }
}
