namespace lucia.Agents.Training.Models;

/// <summary>
/// Captured routing decision from the orchestrator's router executor.
/// </summary>
public sealed class RoutingDecision
{
    public required string SelectedAgentId { get; set; }

    public List<string> AdditionalAgentIds { get; set; } = [];

    public double Confidence { get; set; }

    public string? Reasoning { get; set; }

    public double RoutingDurationMs { get; set; }

    public string? ModelDeploymentName { get; set; }
}
