using lucia.Agents.Abstractions;

namespace lucia.Agents.Mcp;

/// <summary>
/// Provides runtime access to dynamically loaded agents so the orchestrator
/// can resolve their AIAgent instances for local invocation.
/// </summary>
public interface IDynamicAgentProvider
{
    /// <summary>
    /// Gets a dynamic agent by name.
    /// </summary>
    DynamicAgent? GetAgent(string name);

    /// <summary>
    /// Gets all currently registered dynamic agents.
    /// </summary>
    IReadOnlyList<DynamicAgent> GetAllAgents();

    /// <summary>
    /// Registers a dynamic agent instance.
    /// </summary>
    void Register(DynamicAgent agent);

    /// <summary>
    /// Removes all registered dynamic agents.
    /// </summary>
    void Clear();
}
