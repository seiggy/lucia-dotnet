using System.Diagnostics.CodeAnalysis;
using A2A;
using A2A.AspNetCore;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Extensions;
using lucia.Agents.Registry;

namespace lucia.AgentHost.Extensions;

public static class AgentDiscoveryExtension
{
    public static void MapAgentDiscovery(this WebApplication app)
    {
        try
        {
            // Map the orchestrator's A2A endpoint and well-known agent card
            var orchestratorAgent = app.Services.GetServices<ILuciaAgent>()
                .OfType<OrchestratorAgent>()
                .Single();
            var orchestratorCard = orchestratorAgent.GetAgentCard();
            app.MapA2ALazy(() => orchestratorAgent.GetAIAgent(), path: "/agent", agentCard: orchestratorCard);

            // Map A2A endpoints for all other in-process agents with relative paths
            var agents = app.Services.GetServices<ILuciaAgent>();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            foreach (var agent in agents)
            {
                if (agent is OrchestratorAgent)
                    continue; // Already mapped above with well-known card

                var card = agent.GetAgentCard();
                var cardUrl = card.GetUrl();
                if (cardUrl is not null && cardUrl.StartsWith('/'))
                {
                    app.MapA2ALazy(() => agent.GetAIAgent(), path: cardUrl, agentCard: card);
                    logger.LogInformation("Mapped in-process A2A endpoint at {Path} for agent {Name}", cardUrl, card.Name);
                }
            }
        }
        catch (Exception e)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(e, "Error trying to setup agent discovery");
        }
    }

    public static void MapAgentDiscovery(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        var routeGroup = endpoints.MapGroup(path);
        routeGroup.MapGet("/", async (IAgentRegistry agentCatalog, CancellationToken cancellationToken) =>
        {
            var results = new List<AgentCard>();
            await foreach (var result in agentCatalog.GetEnumerableAgentsAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(result);
            }

            return Results.Ok(results);
        }).WithName("GetAgents");
    }

    public static IEndpointConventionBuilder MapAgentDiscoveryEndpoint(this IEndpointRouteBuilder endpoints, AgentCard agentCard,
        [StringSyntax("Route")] string agentDiscoveryPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(agentCard);
        ArgumentException.ThrowIfNullOrEmpty(agentDiscoveryPath);

        var routeGroup = endpoints.MapGroup("");
        A2ARouteBuilderExtensions.MapWellKnownAgentCard(routeGroup, agentCard, agentDiscoveryPath);

        return routeGroup;
    }
}