using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Selected test scope. When <see cref="MaxCasesPerAgent"/> is null, all cases run.
/// </summary>
public sealed record TestSuiteChoice(int? MaxCasesPerAgent);
