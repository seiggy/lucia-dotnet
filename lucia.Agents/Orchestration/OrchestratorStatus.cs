using A2A;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Status information for the orchestrator
/// </summary>
public class OrchestratorStatus
{
    public bool IsReady { get; set; }
    public int AvailableAgentCount { get; set; }
    public List<AgentCard> AvailableAgents { get; set; } = new();
}