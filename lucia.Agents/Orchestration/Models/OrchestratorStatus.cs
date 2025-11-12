using A2A;

namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// Status information for the orchestrator
/// </summary>
public class OrchestratorStatus
{
    public bool IsReady { get; set; }
    public int AvailableAgentCount { get; set; }
    public IReadOnlyCollection<AgentCard> AvailableAgents { get; set; } = [];
}