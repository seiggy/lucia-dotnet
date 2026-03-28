namespace lucia.Tests.Orchestration;

/// <summary>
/// Captures the result of running a single scenario on a single model,
/// including the score, failure classification, and tool call trace.
/// Used internally by <see cref="ModelComparisonReporter"/>.
/// </summary>
public sealed class ModelScenarioResult
{
    /// <summary>The model deployment name (e.g., "gpt-4o", "llama3.2").</summary>
    public required string ModelId { get; init; }

    /// <summary>The scenario name (e.g., "LightAgent.FindLight_SingleLight").</summary>
    public required string ScenarioName { get; init; }

    /// <summary>Numeric score from the primary evaluator (typically 1–5).</summary>
    public required double Score { get; init; }

    /// <summary>Classified failure type, or <see cref="FailureType.None"/> if passed.</summary>
    public required FailureType FailureType { get; init; }

    /// <summary>Names of tools that were actually called during execution.</summary>
    public required IReadOnlyList<string> ToolsCalled { get; init; }
}
