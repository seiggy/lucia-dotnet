namespace lucia.Agents.Models;

/// <summary>
/// Result of a single test case evaluation within a parameter optimization run.
/// </summary>
public sealed record OptimizationCaseResult
{
    /// <summary>The test case that was evaluated.</summary>
    public required OptimizationTestCase TestCase { get; init; }

    /// <summary>Whether all expected entities were found in the results.</summary>
    public bool Found { get; init; }

    /// <summary>Entity IDs from the expected set that were found in results.</summary>
    public IReadOnlyList<string> FoundEntityIds { get; init; } = [];

    /// <summary>Entity IDs from the expected set that were missing from results.</summary>
    public IReadOnlyList<string> MissedEntityIds { get; init; } = [];

    /// <summary>Number of entities returned by the matcher.</summary>
    public int MatchCount { get; init; }

    /// <summary>Whether the match count is within the expected maximum.</summary>
    public bool CountWithinLimit { get; init; }

    /// <summary>
    /// Weighted score for this case. Recall component (3.0 max) is negative
    /// when expected entities are missing. Precision component (1.0 max) rewards
    /// exact result counts and penalizes excess results.
    /// </summary>
    public double CaseScore { get; init; }
}
