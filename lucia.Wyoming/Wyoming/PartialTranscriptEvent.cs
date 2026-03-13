namespace lucia.Wyoming.Wyoming;

public sealed record PartialTranscriptEvent : WyomingEvent
{
    public override string Type => "partial-transcript";

    public string Text { get; init; } = string.Empty;

    public float Confidence { get; init; }

    public bool IsFinal { get; init; }
}
