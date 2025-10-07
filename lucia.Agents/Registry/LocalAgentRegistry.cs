using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using A2A;
using Microsoft.Agents.AI.Hosting;
using System.Linq;
using System.Runtime.CompilerServices;

namespace lucia.Agents.Registry;

/// <summary>
/// In-memory implementation of the agent registry
/// </summary>
public sealed class LocalAgentRegistry : AgentRegistry
{
    // Agents are stored by their URI for quick access
    private readonly ConcurrentDictionary<string, AgentCard> _agents = new();
    private readonly ILogger<AgentRegistry> _logger;

    public LocalAgentRegistry(
        ILogger<AgentRegistry> logger)
    {
        _logger = logger;
    }

    public override Task RegisterAgentAsync(AgentCard agent, CancellationToken cancellationToken = default)
    {
        _agents.AddOrUpdate(agent.Url, agent, (key, existing) => agent);
        _logger.LogInformation("Agent {AgentId} ({AgentName}) registered successfully", agent.Url, agent.Name);
        return Task.CompletedTask;
    }

    public override Task UnregisterAgentAsync(string agentUri, CancellationToken cancellationToken = default)
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

    public override async IAsyncEnumerable<AgentCard> GetAgentsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var name in _agents.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var agent = _agents[name];
            if (agent is not null)
            {
                yield return agent;
            }
        }
    }

    public override Task<AgentCard?> GetAgentAsync(string agentUri, CancellationToken cancellationToken = default)
    {
        _agents.TryGetValue(agentUri, out var agent);
        return Task.FromResult(agent);
    }

    public override async Task<IAsyncEnumerable<AgentCard>> FindCapableAgentsAsync(string userRequest, CancellationToken cancellationToken = default)
    {
        // TODO: Implement intelligent agent selection based on capabilities
        // For now, return all agents - the orchestrator will decide
        var agents = GetAgentsAsync(cancellationToken);
        return agents;
    }
}
