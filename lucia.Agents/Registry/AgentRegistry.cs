using A2A;
using System.Runtime.CompilerServices;

namespace lucia.Agents.Registry;

/// <summary>
/// Registry for managing available agents in the Lucia system
/// </summary>
public abstract class AgentRegistry
{
    /// <summary>
    /// Register an agent with the system
    /// </summary>
    public abstract Task RegisterAgentAsync(AgentCard agent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregister an agent from the system
    /// </summary>
    public abstract Task UnregisterAgentAsync(string agentUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all registered agents
    /// </summary>
    public abstract IAsyncEnumerable<AgentCard> GetAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a specific agent by ID
    /// </summary>
    public abstract Task<AgentCard?> GetAgentAsync(string agentUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find agents that can handle a specific request
    /// </summary>
    public abstract Task<IAsyncEnumerable<AgentCard>> FindCapableAgentsAsync(string userRequest, CancellationToken cancellationToken = default);
}