namespace lucia.Wyoming.WakeWord;

/// <summary>
/// Result from wake word detection.
/// </summary>
public sealed record WakeWordResult
{
    public required string Keyword { get; init; }

    public float Confidence { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
