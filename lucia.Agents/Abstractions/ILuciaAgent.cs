using A2A;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.Agents.AI;

namespace lucia.Agents.Abstractions
{
    /// <summary>
    /// Core abstraction for all Lucia agents (built-in, dynamic, and plugin-based).
    /// Agents are discovered via <c>IEnumerable&lt;ILuciaAgent&gt;</c> and registered
    /// with the <see cref="lucia.Agents.Registry.IAgentRegistry"/> on startup.
    /// </summary>
    public interface ILuciaAgent
    {
        /// <summary>
        /// Returns the A2A agent card describing this agent's identity, capabilities, and skills.
        /// Used for agent discovery and registration with the agent registry.
        /// </summary>
        AgentCard GetAgentCard();

        /// <summary>
        /// Returns the underlying <see cref="AIAgent"/> instance used for LLM interactions.
        /// The agent is constructed during <see cref="InitializeAsync"/> and may be rebuilt
        /// by <see cref="RefreshConfigAsync"/> when provider settings change.
        /// </summary>
        AIAgent GetAIAgent();

        /// <summary>
        /// Initializes the agent by constructing its <see cref="AIAgent"/>, loading entity caches,
        /// and registering tools. Called once during application startup by <c>AgentInitializationService</c>.
        /// </summary>
        /// <param name="cancellationToken">Token to cancel initialization.</param>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-reads the agent's <see cref="AgentDefinition"/>
        /// from the store and rebuilds the underlying <see cref="AIAgent"/> when the
        /// model or embedding provider has changed. Called before every request.
        /// </summary>
        Task RefreshConfigAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
