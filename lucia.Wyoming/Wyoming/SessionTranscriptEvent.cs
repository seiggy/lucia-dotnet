namespace lucia.Wyoming.Wyoming;

public sealed record SessionTranscriptEvent : SessionEvent
{
    public required string Text { get; init; }
    public required float Confidence { get; init; }
    public string? SpeakerId { get; init; }
    public string? SpeakerName { get; init; }

    /// <summary>
    /// False for streaming partial results that may be revised.
    /// True for the finalized transcript after audio stops.
    /// </summary>
    public bool IsFinal { get; init; }
}
