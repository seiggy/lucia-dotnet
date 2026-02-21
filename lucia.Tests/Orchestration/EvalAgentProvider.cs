using lucia.Agents.Orchestration;
using Microsoft.Agents.AI;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Test-only <see cref="IAgentProvider"/> that returns a pre-built list of
/// real <see cref="AIAgent"/> instances for use in orchestrator eval tests.
/// </summary>
public sealed class EvalAgentProvider : IAgentProvider
{
    private readonly IReadOnlyList<AIAgent> _agents;

    public EvalAgentProvider(IEnumerable<AIAgent> agents)
    {
        _agents = agents.ToList().AsReadOnly();
    }

    public Task<IReadOnlyList<AIAgent>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_agents);
    }
}
