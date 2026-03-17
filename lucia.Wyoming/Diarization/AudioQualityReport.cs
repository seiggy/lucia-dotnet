namespace lucia.Wyoming.Diarization;

public sealed record AudioQualityReport
{
    public int DurationMs { get; init; }
    public float RmsEnergy { get; init; }
    public bool IsTooQuiet { get; init; }
    public bool IsTooShort { get; init; }
    public bool IsAcceptable => !IsTooQuiet && !IsTooShort;
}
