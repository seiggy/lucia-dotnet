namespace lucia.Agents.Services;

/// <summary>
/// Result of a single test case evaluation within a parameter optimization run.
/// </summary>
public sealed record OptimizationCaseResult
{
    /// <summary>The test case that was evaluated.</summary>
    public required OptimizationTestCase TestCase { get; init; }

    /// <summary>Whether the expected entity was found in the results.</summary>
    public bool Found { get; init; }

    /// <summary>Number of entities returned by the matcher.</summary>
    public int MatchCount { get; init; }

    /// <summary>Whether the match count is within the expected maximum.</summary>
    public bool CountWithinLimit { get; init; }

    /// <summary>Weighted score for this case (found × 3 + countOk × 1).</summary>
    public double CaseScore { get; init; }
}
