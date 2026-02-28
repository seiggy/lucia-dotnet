namespace lucia.Agents.Services;

/// <summary>
/// Progress snapshot emitted during parameter optimization.
/// Sent to callers (API polling, test output) to track optimizer state.
/// </summary>
public sealed record OptimizationProgress
{
    /// <summary>Current coordinate descent iteration (starts at 1).</summary>
    public int Iteration { get; init; }

    /// <summary>Current weighted score across all test cases.</summary>
    public double CurrentScore { get; init; }

    /// <summary>Maximum achievable score (testCases × maxScorePerCase).</summary>
    public double MaxScore { get; init; }

    /// <summary>Current best parameter set.</summary>
    public required HybridMatchOptions BestParams { get; init; }

    /// <summary>Current step size for coordinate descent.</summary>
    public double Step { get; init; }

    /// <summary>Number of unique parameter points evaluated so far.</summary>
    public int EvaluatedPoints { get; init; }

    /// <summary>Whether the optimizer has finished (converged or perfect score).</summary>
    public bool IsComplete { get; init; }

    /// <summary>Optional status message (e.g. "halving step", "walking threshold").</summary>
    public string? Message { get; init; }
}
