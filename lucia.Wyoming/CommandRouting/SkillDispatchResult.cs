namespace lucia.Wyoming.CommandRouting;

public sealed record SkillDispatchResult
{
    public required bool Success { get; init; }

    public required string ResponseText { get; init; }

    public required string SkillId { get; init; }

    public TimeSpan ExecutionDuration { get; init; }

    public string? Error { get; init; }

    public static SkillDispatchResult Failed(string skillId, string error, TimeSpan duration) => new()
    {
        Success = false,
        ResponseText = "Sorry, something went wrong processing your command.",
        SkillId = skillId,
        Error = error,
        ExecutionDuration = duration,
    };
}
