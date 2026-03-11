namespace lucia.Evals;

/// <summary>
/// A single evaluation scenario that can be executed by the runner.
/// </summary>
public sealed record EvalScenario
{
    public required string Name { get; init; }
    public required string Group { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required Func<Task<EvalScenarioResult>> RunAsync { get; init; }
}
