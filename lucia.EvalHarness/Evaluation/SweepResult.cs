namespace lucia.EvalHarness.Evaluation;

public sealed class SweepResult
{
    public required string RunId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required IReadOnlyList<ModelEvalResult> BaselineResults { get; init; }
    public required double? BaselineMeanScore { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<SweepEntry>> TargetResults { get; init; }
}
