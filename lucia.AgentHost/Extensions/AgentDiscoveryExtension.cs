using System.Diagnostics.CodeAnalysis;
using A2A;
using A2A.AspNetCore;
using lucia.Agents.Agents;
using lucia.Agents.Registry;
using Microsoft.Agents.AI.A2A;

namespace lucia.AgentHost.Extensions;

public static class AgentDiscoveryExtension
{
    public static void MapAgentDiscovery(this WebApplication app)
    {
        var lightAgent = app.Services.GetRequiredService<LightAgent>();

        var lightAgentHost = new A2AHostAgent(lightAgent.GetAIAgent(), lightAgent.GetAgentCard());
        
        app.MapA2A(lightAgentHost.TaskManager, lightAgent.GetAgentCard().Url);
        app.MapWellKnownAgentCard(lightAgentHost.TaskManager, lightAgent.GetAgentCard().Url);

    }
}