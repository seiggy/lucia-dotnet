namespace lucia.Wyoming.Vad;

/// <summary>
/// Represents a detected speech segment.
/// </summary>
public sealed record VadSegment
{
    public TimeSpan StartTime { get; init; }

    public TimeSpan EndTime { get; init; }

    public int SampleRate { get; init; }

    public float[] Samples { get; init; } = [];
}
