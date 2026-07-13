namespace lucia.EvalHarness.Evaluation;

public sealed class TestCaseResult
{
    public required string TestCaseId { get; init; }
    public required bool Passed { get; init; }
    public required double? Score { get; init; }
    public required TimeSpan Latency { get; init; }
    public string? FailureReason { get; init; }
    public string? AgentOutput { get; init; }
    public string? Input { get; init; }
    public IReadOnlyList<ToolCallTrace>? ToolCalls { get; init; }
    public IReadOnlyList<ConversationTurn>? ConversationHistory { get; init; }
    public string? JudgeStatus { get; init; }
    public string? JudgeReason { get; init; }
}
