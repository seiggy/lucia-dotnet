namespace lucia.Evals;

/// <summary>
/// The outcome of a single evaluation scenario execution.
/// </summary>
public sealed record EvalScenarioResult
{
    public required bool Passed { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
    public bool Skipped { get; init; }

    public static EvalScenarioResult Pass(TimeSpan duration, string message, string? details = null) =>
        new() { Passed = true, Duration = duration, Message = message, Details = details };

    public static EvalScenarioResult Fail(TimeSpan duration, string message, string? details = null) =>
        new() { Passed = false, Duration = duration, Message = message, Details = details };

    public static EvalScenarioResult Skip(string reason) =>
        new() { Passed = false, Skipped = true, Duration = TimeSpan.Zero, Message = reason };
}
