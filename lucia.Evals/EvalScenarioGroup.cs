namespace lucia.Evals;

/// <summary>
/// Groups related evaluation scenarios for menu selection and reporting.
/// </summary>
public sealed record EvalScenarioGroup
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<EvalScenario> Scenarios { get; init; }
}
