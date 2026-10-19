namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// What the orchestrator did for one scenario: who it routed to, what it told that agent,
/// and what the agent said back. Lets a report separate router faults from agent faults.
/// </summary>
public sealed class RoutingTrace
{
    public string? AgentId { get; init; }
    public double Confidence { get; init; }
    public string? Reasoning { get; init; }
    public IReadOnlyDictionary<string, string> Instructions { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> AgentResponses { get; init; } = new Dictionary<string, string>();
}
