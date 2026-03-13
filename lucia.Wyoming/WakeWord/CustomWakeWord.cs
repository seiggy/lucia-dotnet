namespace lucia.Wyoming.WakeWord;

public sealed record CustomWakeWord
{
    public required string Id { get; init; }
    public required string Phrase { get; init; }
    public required string Tokens { get; init; }
    public string? UserId { get; init; }
    public float BoostScore { get; init; } = 1.5f;
    public float Threshold { get; init; } = 0.30f;
    public bool IsDefault { get; init; }
    public bool IsCalibrated { get; init; }
    public DateTimeOffset? CalibratedAt { get; init; }
    public int CalibrationSamples { get; init; }
    public float? DetectionRate { get; init; }
    public float? AverageConfidence { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
