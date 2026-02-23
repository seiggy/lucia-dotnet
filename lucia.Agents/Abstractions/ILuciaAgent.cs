using A2A;
using Microsoft.Agents.AI;

namespace lucia.Agents.Abstractions
{
    public interface ILuciaAgent
    {
        AgentCard GetAgentCard();
        AIAgent GetAIAgent();
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }
}
