namespace lucia.EvalHarness.Evaluation;

public sealed class EvalRunResult
{
    public required string RunId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required IReadOnlyList<AgentEvalResult> AgentResults { get; init; }
}
