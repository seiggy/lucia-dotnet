namespace lucia.Wyoming.WakeWord;

public sealed record CalibrationResult
{
    public required float DetectionRate { get; init; }
    public required float AverageConfidence { get; init; }
    public required float BoostScore { get; init; }
    public required float Threshold { get; init; }
    public required string Recommendation { get; init; }
}
