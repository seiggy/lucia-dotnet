using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Test double for agent catalog functionality.
/// AgentCatalog was removed in MAF 1.0.0-preview.260212.1.
/// </summary>
internal sealed class StubAgentCatalog
{
    private readonly IReadOnlyDictionary<string, AIAgent> _agentsByName;

    public StubAgentCatalog(IEnumerable<AIAgent> agents)
    {
        if (agents is null)
        {
            throw new ArgumentNullException(nameof(agents));
        }

        _agentsByName = agents.ToDictionary(
            agent => agent.Name ?? agent.Id,
            agent => agent,
            StringComparer.OrdinalIgnoreCase);
    }

    public async IAsyncEnumerable<AIAgent> GetAgentsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var agent in _agentsByName.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return agent;
            await Task.Yield();
        }
    }
}
