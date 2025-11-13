using System.Diagnostics.CodeAnalysis;
using A2A;
using A2A.AspNetCore;
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
            var orchestratorAgent = app.Services.GetRequiredService<OrchestratorAgent>();

            var taskManager = app.Services.GetRequiredService<ITaskManager>();

            app.MapA2A(orchestratorAgent.GetAIAgent(), path: "/agent", agentCard: orchestratorAgent.GetAgentCard(), 
                taskManager => app.MapWellKnownAgentCard(taskManager, "/agent"));
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