namespace lucia.EvalHarness.Personality;

/// <summary>
/// Result of evaluating a single (scenario, profile, model) combination.
/// </summary>
public sealed class PersonalityScenarioResult
{
    public required string ScenarioId { get; init; }
    public required string ScenarioDescription { get; init; }
    public required string Category { get; init; }
    public required string ProfileId { get; init; }
    public required string ProfileName { get; init; }
    public required string ModelName { get; init; }
    public required bool Passed { get; init; }
    public required IReadOnlyList<string> FailedChecks { get; init; }
    public required string LlmResponse { get; init; }
    public required long DurationMs { get; init; }
}
