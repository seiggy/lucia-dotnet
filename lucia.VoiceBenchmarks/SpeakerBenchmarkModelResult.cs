namespace lucia.VoiceBenchmarks;

public sealed class SpeakerBenchmarkModelResult
{
    public string ModelPath { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string ModelSha256 { get; init; } = string.Empty;
    public int EmbeddingDimension { get; init; }
    public int SpeakerCount { get; init; }
    public int EnrollmentClipCount { get; init; }
    public int TestClipCount { get; init; }
    public double Top1Accuracy { get; init; }
    public double EqualErrorRate { get; init; }
    public double MeanRealTimeFactor { get; init; }
    public double WallDurationSeconds { get; init; }
    public double CpuUtilizationPercent { get; init; }
    public long ManagedAllocationDeltaBytes { get; init; }
    public long WorkingSetBeforeBytes { get; init; }
    public long WorkingSetAfterBytes { get; init; }
    public IReadOnlyList<double> GenuineScores { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> ImpostorScores { get; init; } = Array.Empty<double>();
    public IReadOnlyList<SpeakerBenchmarkPrediction> Predictions { get; init; } = Array.Empty<SpeakerBenchmarkPrediction>();
}
