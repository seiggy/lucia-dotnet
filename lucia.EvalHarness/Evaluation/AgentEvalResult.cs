namespace lucia.EvalHarness.Evaluation;

public sealed class AgentEvalResult
{
    public required string AgentName { get; init; }
    public required IReadOnlyList<ModelEvalResult> ModelResults { get; init; }
}
