using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using lucia.Agents.A2A;

namespace lucia.Agents.Registry;

/// <summary>
/// In-memory implementation of the agent registry
/// </summary>
public class AgentRegistry : IAgentRegistry
{
    // Agents are stored by their URI for quick access
    private readonly ConcurrentDictionary<string, AgentCard> _agents = new();
    private readonly ILogger<AgentRegistry> _logger;

    public AgentRegistry(ILogger<AgentRegistry> logger)
    {
        _logger = logger;
    }

    public Task RegisterAgentAsync(AgentCard agent, CancellationToken cancellationToken = default)
    {
        _agents.AddOrUpdate(agent.Uri, agent, (key, existing) => agent);
        _logger.LogInformation("Agent {AgentId} ({AgentName}) registered successfully", agent.Uri, agent.Name);
        return Task.CompletedTask;
    }

    public Task UnregisterAgentAsync(string agentUri, CancellationToken cancellationToken = default)
    {
        if (_agents.TryRemove(agentUri, out var agent))
        {
            _logger.LogInformation("Agent {AgentUri} ({AgentName}) unregistered successfully", agentUri, agent.Name);
        }
        else
        {
            _logger.LogWarning("Attempted to unregister non-existent agent {AgentId}", agentUri);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<AgentCard>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        var agents = _agents.Values.ToList();
        return Task.FromResult<IReadOnlyCollection<AgentCard>>(agents);
    }

    public Task<AgentCard?> GetAgentAsync(string agentUri, CancellationToken cancellationToken = default)
    {
        _agents.TryGetValue(agentUri, out var agent);
        return Task.FromResult(agent);
    }

    public async Task<IReadOnlyCollection<AgentCard>> FindCapableAgentsAsync(string userRequest, CancellationToken cancellationToken = default)
    {
        // TODO: Implement intelligent agent selection based on capabilities
        // For now, return all agents - the orchestrator will decide
        var agents = await GetAgentsAsync(cancellationToken);
        return agents;
    }
}