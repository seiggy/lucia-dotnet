using A2A;
using Microsoft.Agents.AI;
using System;
using System.Collections.Generic;
using System.Text;

namespace lucia.Agents.Abstractions
{
    public interface ILuciaAgent
    {
        AgentCard GetAgentCard();
        AIAgent GetAIAgent();
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }
}
