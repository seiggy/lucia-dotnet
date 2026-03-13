namespace lucia.Wyoming.CommandRouting;

public sealed record CommandRouteResult
{
    public required bool IsMatch { get; init; }

    public required float Confidence { get; init; }

    public CommandPattern? MatchedPattern { get; init; }

    public IReadOnlyDictionary<string, string>? CapturedValues { get; init; }

    public string? ResolvedEntityId { get; init; }

    public string? ResolvedAreaId { get; init; }

    public string? SpeakerId { get; init; }

    public TimeSpan MatchDuration { get; init; }

    public static CommandRouteResult NoMatch(TimeSpan duration) => new()
    {
        IsMatch = false,
        Confidence = 0,
        MatchDuration = duration,
    };
}
