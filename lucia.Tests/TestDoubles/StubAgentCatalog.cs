using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;

namespace lucia.Tests.TestDoubles;

internal sealed class StubAgentCatalog : AgentCatalog
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

    public override async IAsyncEnumerable<AIAgent> GetAgentsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var agent in _agentsByName.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return agent;
            await Task.Yield();
        }
    }
}
