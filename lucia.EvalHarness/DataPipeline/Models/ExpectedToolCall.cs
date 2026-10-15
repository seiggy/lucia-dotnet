namespace lucia.EvalHarness.DataPipeline.Models;

/// <summary>
/// Represents an expected tool call for evaluation.
/// </summary>
public sealed class ExpectedToolCall
{
    public required string Tool { get; set; }

    public Dictionary<string, string> Arguments { get; set; } = [];
}
