using A2A;
using Microsoft.Agents.AI;

namespace lucia.Agents.Abstractions
{
    public interface ILuciaAgent
    {
        AgentCard GetAgentCard();
        AIAgent GetAIAgent();
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-reads the agent's <see cref="lucia.Agents.Configuration.AgentDefinition"/>
        /// from the store and rebuilds the underlying <see cref="AIAgent"/> when the
        /// model or embedding provider has changed. Called before every request.
        /// </summary>
        Task RefreshConfigAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
