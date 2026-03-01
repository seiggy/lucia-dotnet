namespace lucia.Agents.Services;

/// <summary>
/// Final result of a skill parameter optimization run, containing the
/// best parameters found and detailed per-test-case breakdown.
/// </summary>
public sealed record OptimizationResult
{
    /// <summary>The optimal parameters found by the optimizer.</summary>
    public required HybridMatchOptions BestParams { get; init; }

    /// <summary>Weighted score achieved with <see cref="BestParams"/>.</summary>
    public double Score { get; init; }

    /// <summary>Maximum achievable score across all test cases.</summary>
    public double MaxScore { get; init; }

    /// <summary>Per-test-case results at the optimal parameter point.</summary>
    public required IReadOnlyList<OptimizationCaseResult> CaseResults { get; init; }

    /// <summary>Total number of unique parameter points evaluated.</summary>
    public int TotalEvaluatedPoints { get; init; }

    /// <summary>Total iterations of coordinate descent performed.</summary>
    public int TotalIterations { get; init; }

    /// <summary>Whether a perfect score was achieved (Score == MaxScore).</summary>
    public bool IsPerfect => Score >= MaxScore;
}
