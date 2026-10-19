namespace lucia.EvalHarness.Evaluation;

/// <summary>An expected tool call with optional argument assertions and equivalent alternatives.</summary>
public sealed class ExpectedToolCall
{
    /// <summary>The function name, with or without the Async suffix.</summary>
    public required string Tool { get; init; }

    /// <summary>Use "*" for any value or "contains:text" for a case-insensitive substring.</summary>
    public Dictionary<string, string> Arguments { get; init; } = [];

    public List<ExpectedToolCall> Alternatives { get; init; } = [];

    /// <summary>
    /// When true, the call may be absent. A matching call is consumed and checked;
    /// otherwise the next expectation is compared with the same actual call.
    /// </summary>
    public bool Optional { get; init; }
}
