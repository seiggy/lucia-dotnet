namespace lucia.Wyoming.Wyoming;

internal sealed class EnhancedClipSttResult
{
    public required string Text { get; init; }

    public float Confidence { get; init; }

    public long DurationMs { get; init; }
}
