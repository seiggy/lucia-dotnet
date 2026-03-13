namespace lucia.Wyoming.Wyoming;

public sealed record TranscriptEvent : WyomingEvent
{
    public override string Type => "transcript";

    public string Text { get; init; } = string.Empty;

    public float Confidence { get; init; }
}
