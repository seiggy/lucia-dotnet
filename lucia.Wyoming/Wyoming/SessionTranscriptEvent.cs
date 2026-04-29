namespace lucia.Wyoming.Wyoming;

public sealed record SessionTranscriptEvent : SessionEvent
{
    public required string Text { get; init; }
    public required float Confidence { get; init; }
    public string? SpeakerId { get; init; }
    public string? SpeakerName { get; init; }

    /// <summary>
    /// Whether STT used "raw" or "enhanced" (GTCRN-denoised) audio for this transcript.
    /// </summary>
    public string AudioSource { get; init; } = "raw";

    /// <summary>
    /// False for streaming partial results that may be revised.
    /// True for the finalized transcript after audio stops.
    /// </summary>
    public bool IsFinal { get; init; }
}
