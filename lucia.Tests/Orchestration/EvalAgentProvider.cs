using System.Collections.Concurrent;
using System.Collections.Immutable;
using lucia.Agents.Abstractions;
using Microsoft.Agents.AI;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Test-only <see cref="IAgentProvider"/> that returns a pre-built list of
/// real <see cref="AIAgent"/> instances for use in orchestrator eval tests.
/// </summary>
public sealed class EvalAgentProvider : IAgentProvider
{
    private readonly ImmutableList<AIAgent> _agents;

    public EvalAgentProvider(IEnumerable<AIAgent> agents)
    {
        _agents = ImmutableList.CreateRange(agents);
    }

    public ImmutableList<AIAgent> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        return _agents;
    }
}
