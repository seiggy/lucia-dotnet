using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AgentCard = A2A.AgentCard;
using lucia.Agents.Registry;

namespace lucia.Tests.TestDoubles;

internal sealed class StaticAgentRegistry : AgentRegistry
{
    private readonly IReadOnlyList<AgentCard> _agents;

    public StaticAgentRegistry(IReadOnlyList<AgentCard> agents)
    {
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
    }

    public override Task RegisterAgentAsync(AgentCard agent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override Task UnregisterAgentAsync(string agentUri, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public override async IAsyncEnumerable<AgentCard> GetAgentsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var agent in _agents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return agent;
            await Task.Yield();
        }
    }

    public override Task<AgentCard?> GetAgentAsync(string agentUri, CancellationToken cancellationToken = default)
    {
        var agent = _agents.FirstOrDefault(a => string.Equals(a.Url, agentUri, StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(a.Name, agentUri, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(agent);
    }

    public override Task<IAsyncEnumerable<AgentCard>> FindCapableAgentsAsync(string userRequest, CancellationToken cancellationToken = default)
        => Task.FromResult<IAsyncEnumerable<AgentCard>>(GetAgentsAsync(cancellationToken));
}
