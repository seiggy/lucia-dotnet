using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using A2A;

namespace lucia.Agents.Registry;

/// <summary>
/// In-memory implementation of the agent registry
/// </summary>
public sealed class LocalAgentRegistry : IAgentRegistry
{
    // Agents are stored by their URI for quick access
    private readonly ConcurrentDictionary<string, AgentCard> _agents = new();
    private readonly ILogger<LocalAgentRegistry> _logger;

    public LocalAgentRegistry(
        ILogger<LocalAgentRegistry> logger)
    {
        _logger = logger;
    }

    public Task RegisterAgentAsync(AgentCard agent, CancellationToken cancellationToken = default)
    {
        _agents.AddOrUpdate(agent.Url, agent, (key, existing) => agent);
        _logger.LogInformation("Agent {AgentId} ({AgentName}) registered successfully", agent.Url, agent.Name);
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


    public Task<AgentCard?> GetAgentAsync(string agentUri, CancellationToken cancellationToken = default)
    {
        _agents.TryGetValue(agentUri, out var agent);
        return Task.FromResult(agent);
    }

    public async IAsyncEnumerable<AgentCard> GetEnumerableAgentsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
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

    public Task<IReadOnlyCollection<AgentCard>> GetAllAgentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_agents.Values.ToList() as IReadOnlyCollection<AgentCard>);
    }
}
