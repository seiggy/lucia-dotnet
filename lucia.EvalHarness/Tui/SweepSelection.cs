using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// The user's sweep experiment configuration choices.
/// </summary>
public sealed class SweepSelection
{
    public required string BaselineModel { get; init; }
    public required IReadOnlyList<string> TargetModels { get; init; }
    public required IReadOnlyList<string> AgentNames { get; init; }
    public required ParameterSweepConfig Config { get; init; }
}
