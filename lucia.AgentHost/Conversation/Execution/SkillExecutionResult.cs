namespace lucia.AgentHost.Conversation.Execution;

/// <summary>
/// Represents the outcome of a direct skill execution that bypasses LLM processing.
/// </summary>
public sealed record SkillExecutionResult
{
    /// <summary>Whether the skill executed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>The skill that was invoked (e.g., "light-control").</summary>
    public required string SkillId { get; init; }

    /// <summary>The action that was dispatched (e.g., "toggle").</summary>
    public required string Action { get; init; }

    /// <summary>Values captured from the matched command pattern.</summary>
    public IReadOnlyDictionary<string, string> Captures { get; init; } = new Dictionary<string, string>();

    /// <summary>The response text returned by the skill method, if execution succeeded.</summary>
    public string? ResponseText { get; init; }

    /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
    public string? Error { get; init; }

    /// <summary>Wall-clock duration of the skill execution.</summary>
    public TimeSpan ExecutionDuration { get; init; }

    /// <summary>Creates a failed result with the given error details.</summary>
    public static SkillExecutionResult Failed(string skillId, string action, string error, TimeSpan duration) => new()
    {
        Success = false,
        SkillId = skillId,
        Action = action,
        Error = error,
        ExecutionDuration = duration,
    };
}
