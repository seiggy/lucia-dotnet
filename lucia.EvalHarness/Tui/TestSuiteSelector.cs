using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Interactive selection for test suite scope.
/// </summary>
public static class TestSuiteSelector
{
    public static TestSuiteChoice Select()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cornflowerblue]Select test scope[/]")
                .AddChoices(
                    "All test cases (full evaluation)",
                    "Quick smoke test (first 2 cases per agent)",
                    "Custom (choose count per agent)"));

        return choice switch
        {
            "Quick smoke test (first 2 cases per agent)" => new TestSuiteChoice(2),
            "Custom (choose count per agent)" => new TestSuiteChoice(
                AnsiConsole.Prompt(
                    new TextPrompt<int>("[cornflowerblue]Max test cases per agent:[/]")
                        .DefaultValue(5)
                        .Validate(n => n > 0 ? ValidationResult.Success() : ValidationResult.Error("Must be > 0")))),
            _ => new TestSuiteChoice(null)
        };
    }
}

/// <summary>
/// Selected test scope. When <see cref="MaxCasesPerAgent"/> is null, all cases run.
/// </summary>
public sealed record TestSuiteChoice(int? MaxCasesPerAgent);
