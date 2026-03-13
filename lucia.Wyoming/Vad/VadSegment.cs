namespace lucia.Wyoming.Vad;

/// <summary>
/// Represents a detected speech segment.
/// </summary>
public sealed record VadSegment
{
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public float[] Samples { get; init; } = [];
}
