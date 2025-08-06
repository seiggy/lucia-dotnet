namespace lucia.Agents.A2A.Services;

public interface IA2AService
{
    Task<AgentCard> DownloadAgentCardAsync(
        string agentUri,
        CancellationToken cancellationToken = default);
}