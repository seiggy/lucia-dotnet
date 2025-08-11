using A2A;

namespace lucia.Agents.Registry;

/// <summary>
/// Registry for managing available agents in the Lucia system
/// </summary>
public interface IAgentRegistry
{
    /// <summary>
    /// Register an agent with the system
    /// </summary>
    Task RegisterAgentAsync(AgentCard agent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Unregister an agent from the system
    /// </summary>
    Task UnregisterAgentAsync(string agentUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all registered agents
    /// </summary>
    Task<IReadOnlyCollection<AgentCard>> GetAgentsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a specific agent by ID
    /// </summary>
    Task<AgentCard?> GetAgentAsync(string agentUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Find agents that can handle a specific request
    /// </summary>
    Task<IReadOnlyCollection<AgentCard>> FindCapableAgentsAsync(string userRequest, CancellationToken cancellationToken = default);
}