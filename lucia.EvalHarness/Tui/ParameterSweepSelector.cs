using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Interactive TUI for configuring and launching parameter sweep experiments.
/// </summary>
public static class ParameterSweepSelector
{
    /// <summary>
    /// Prompts the user to configure a parameter sweep experiment.
    /// Returns null if the user opts out.
    /// </summary>
    public static SweepSelection? Select(
        IReadOnlyList<OllamaModelInfo> availableModels,
        IReadOnlyList<string> availableAgents)
    {
        if (!AnsiConsole.Confirm(
                "[cornflowerblue]Run parameter sweep experiment?[/] " +
                "(tests multiple parameter configs to find optimal settings for small models)",
                defaultValue: false))
        {
            return null;
        }

        AnsiConsole.WriteLine();

        // Select baseline model (largest/best)
        var baselineModel = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cornflowerblue]Select baseline model[/] (largest/best quality):")
                .AddChoices(availableModels.Select(m => m.Name)));

        // Select target models (smallest)
        var targetModels = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cornflowerblue]Select target models[/] (small models to optimize):")
                .PageSize(15)
                .InstructionsText("[dim](Space to toggle, Enter to confirm)[/]")
                .AddChoices(availableModels.Select(m => m.Name).Where(n => n != baselineModel)));

        if (targetModels.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No target models selected.[/]");
            return null;
        }

        // Select agents to test
        var agentPrompt = new MultiSelectionPrompt<string>()
            .Title("[cornflowerblue]Select agents for sweep[/]:")
            .AddChoices(availableAgents);
        foreach (var a in availableAgents) agentPrompt.Select(a);
        var agents = AnsiConsole.Prompt(agentPrompt);

        // Configure sweep dimensions
        var config = new ParameterSweepConfig { BaselineModel = baselineModel };

        if (AnsiConsole.Confirm("[cornflowerblue]Use default sweep dimensions?[/]", defaultValue: true))
        {
            AnsiConsole.MarkupLine($"[dim]  Temperature: [{string.Join(", ", config.TemperatureValues)}][/]");
            AnsiConsole.MarkupLine($"[dim]  Top-K: [{string.Join(", ", config.TopKValues)}][/]");
            AnsiConsole.MarkupLine($"[dim]  Top-P: [{string.Join(", ", config.TopPValues)}][/]");
            AnsiConsole.MarkupLine($"[dim]  Repeat Penalty: [{string.Join(", ", config.RepeatPenaltyValues)}][/]");
        }
        else
        {
            config.TemperatureValues = PromptDoubleList("Temperature values", config.TemperatureValues);
            config.TopKValues = PromptIntList("Top-K values", config.TopKValues);
            config.TopPValues = PromptDoubleList("Top-P values", config.TopPValues);
            config.RepeatPenaltyValues = PromptDoubleList("Repeat Penalty values", config.RepeatPenaltyValues);
        }

        config.MaxCombinations = AnsiConsole.Prompt(
            new TextPrompt<int>("[cornflowerblue]Max parameter combinations per model[/]:")
                .DefaultValue(config.MaxCombinations));

        var totalCombinations = config.GenerateCombinations().Count;
        var totalRuns = totalCombinations * targetModels.Count * agents.Count;
        AnsiConsole.MarkupLine($"[dim]  {totalCombinations} parameter combinations × {targetModels.Count} models × {agents.Count} agents = {totalRuns} total evaluation runs[/]");
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("[bold]Proceed with sweep?[/]"))
            return null;

        return new SweepSelection
        {
            BaselineModel = baselineModel,
            TargetModels = targetModels,
            AgentNames = agents,
            Config = config
        };
    }

    private static List<double> PromptDoubleList(string label, List<double> defaults)
    {
        var input = AnsiConsole.Prompt(
            new TextPrompt<string>($"  [cornflowerblue]{label}[/] (comma-separated):")
                .DefaultValue(string.Join(", ", defaults)));
        return input.Split(',').Select(s => double.Parse(s.Trim())).ToList();
    }

    private static List<int> PromptIntList(string label, List<int> defaults)
    {
        var input = AnsiConsole.Prompt(
            new TextPrompt<string>($"  [cornflowerblue]{label}[/] (comma-separated):")
                .DefaultValue(string.Join(", ", defaults)));
        return input.Split(',').Select(s => int.Parse(s.Trim())).ToList();
    }
}
