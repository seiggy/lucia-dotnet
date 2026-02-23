using System.Collections.Concurrent;

namespace lucia.Agents.Mcp;

/// <summary>
/// Thread-safe in-memory store for dynamically loaded agents.
/// Used by the orchestrator to resolve DynamicAgent instances at invocation time.
/// </summary>
public sealed class DynamicAgentProvider : IDynamicAgentProvider
{
    private readonly ConcurrentDictionary<string, DynamicAgent> _agents = new(StringComparer.OrdinalIgnoreCase);

    public DynamicAgent? GetAgent(string name)
    {
        _agents.TryGetValue(name, out var agent);
        return agent;
    }

    public IReadOnlyList<DynamicAgent> GetAllAgents()
    {
        return _agents.Values.ToList();
    }

    public void Register(DynamicAgent agent)
    {
        var name = agent.GetAgentCard().Name;
        _agents[name] = agent;
    }

    public void Clear()
    {
        _agents.Clear();
    }

    public bool Unregister(string name)
    {
        return _agents.TryRemove(name, out _);
    }
}
