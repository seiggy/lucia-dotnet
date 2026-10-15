using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// A tool/function call made by the assistant.
/// </summary>
public sealed class ToolCallInfo
{
    public string? CallId { get; init; }
    public required string Name { get; init; }
    public Dictionary<string, string?>? Arguments { get; init; }
}
