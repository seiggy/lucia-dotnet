namespace lucia.EvalHarness.Evaluation;

public sealed class ToolCallTrace
{
    public required string ToolName { get; init; }
    public required int Order { get; init; }
    public Dictionary<string, object?>? Arguments { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public double? DurationMs { get; init; }
}
