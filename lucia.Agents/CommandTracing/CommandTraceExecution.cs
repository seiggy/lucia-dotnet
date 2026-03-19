namespace lucia.Agents.CommandTracing;

/// <summary>
/// Details of the direct skill execution phase.
/// Includes the individual tool calls made to skill methods.
/// </summary>
public sealed record CommandTraceExecution
{
    public required string SkillId { get; init; }
    public required string Action { get; init; }
    public required double DurationMs { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>JSON-serialized high-level parameters derived from the match.</summary>
    public string? ParametersJson { get; init; }

    /// <summary>Final response text returned by the skill execution.</summary>
    public string? ResponseText { get; init; }

    /// <summary>Individual skill method calls made during execution.</summary>
    public IReadOnlyList<CommandTraceToolCall> ToolCalls { get; init; } = [];
}
