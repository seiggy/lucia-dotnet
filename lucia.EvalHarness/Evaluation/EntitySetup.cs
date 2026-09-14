namespace lucia.EvalHarness.Evaluation;

/// <summary>An entity's initial state and optional area assignment in the HA snapshot.</summary>
public sealed class EntitySetup
{
    public required string State { get; init; }
    public string? Area { get; init; }
    public Dictionary<string, object>? Attributes { get; init; }
}
