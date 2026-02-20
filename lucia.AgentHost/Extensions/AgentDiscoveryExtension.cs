using System.Diagnostics.CodeAnalysis;
using A2A;
using A2A.AspNetCore;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Registry;
using Microsoft.Agents.AI.A2A;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;

namespace lucia.AgentHost.Extensions;

public static class AgentDiscoveryExtension
{
    public static void MapAgentDiscovery(this WebApplication app)
    {
        try
        {
            var taskManager = app.Services.GetRequiredService<ITaskManager>();

            // Map the orchestrator's A2A endpoint and well-known agent card
            var orchestratorAgent = app.Services.GetServices<ILuciaAgent>()
                .OfType<OrchestratorAgent>()
                .Single();
            var orchestratorCard = orchestratorAgent.GetAgentCard();
            app.MapA2A(orchestratorAgent.GetAIAgent(), path: "/agent", agentCard: orchestratorCard,
                taskManager => app.MapWellKnownAgentCard(taskManager, "/agent"));

            // Map A2A endpoints for all other in-process agents with relative paths
            var agents = app.Services.GetServices<ILuciaAgent>();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            foreach (var agent in agents)
            {
                if (agent is OrchestratorAgent)
                    continue; // Already mapped above with well-known card

                var card = agent.GetAgentCard();
                if (card.Url is not null && card.Url.StartsWith('/'))
                {
                    app.MapA2A(agent.GetAIAgent(), path: card.Url, agentCard: card);
                    logger.LogInformation("Mapped in-process A2A endpoint at {Path} for agent {Name}", card.Url, card.Name);
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

    public static IEndpointConventionBuilder MapAgentDiscoveryEndpoint(this IEndpointRouteBuilder endpoints, ITaskManager taskManager,
        [StringSyntax("Route")] string agentDiscoveryPath, [StringSyntax("Route")] string agentHostPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentException.ThrowIfNullOrEmpty(agentDiscoveryPath);

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapGet($"{agentDiscoveryPath}/.well-known/agent-card.json", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var agentUrl = $"{request.Scheme}://{request.Host}{agentHostPath}";
            var agentCard = await taskManager.OnAgentCardQuery(agentUrl, cancellationToken);
            return Results.Ok(agentCard);
        });

        return routeGroup;
    }
}