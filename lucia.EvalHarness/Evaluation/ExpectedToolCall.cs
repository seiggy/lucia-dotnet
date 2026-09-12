namespace lucia.EvalHarness.Evaluation;

/// <summary>An expected tool call with optional argument assertions and equivalent alternatives.</summary>
public sealed class ExpectedToolCall
{
    /// <summary>The function name, with or without the Async suffix.</summary>
    public required string Tool { get; init; }

    /// <summary>Use "*" for any value or "contains:text" for a case-insensitive substring.</summary>
    public Dictionary<string, string> Arguments { get; init; } = [];

    public List<ExpectedToolCall> Alternatives { get; init; } = [];
}
