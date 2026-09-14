namespace lucia.EvalHarness.Evaluation;

/// <summary>Post-scenario state assertion for an entity.</summary>
public sealed class EntityStateAssertion
{
    public required string State { get; init; }
    public Dictionary<string, string>? Attributes { get; init; }
}
