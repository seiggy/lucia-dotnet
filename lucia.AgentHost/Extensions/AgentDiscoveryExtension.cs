using System.Diagnostics.CodeAnalysis;
using A2A;
using lucia.Agents.Agents;
using lucia.Agents.Registry;
using Microsoft.Agents.AI.A2A;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Hosting.A2A.AspNetCore;

namespace lucia.AgentHost.Extensions;

public static class AgentDiscoveryExtension
{
    public static void MapAgentDiscovery(this WebApplication app)
    {
        var lightAgent = app.Services.GetRequiredService<LightAgent>();
        var musicAgent = app.Services.GetRequiredService<MusicAgent>();
        var orchestratorAgent = app.Services.GetRequiredService<OrchestratorAgent>();

        var taskManager = app.Services.GetRequiredService<ITaskManager>();
        
        
        app.MapA2A("light-agent", path: "/a2a/light-agent", agentCard: lightAgent.GetAgentCard());
        app.MapA2A("music-agent", path: "/a2a/music-agent", agentCard: musicAgent.GetAgentCard());
        app.MapA2A("orchestrator", path: "/a2a/orchestrator", agentCard: orchestratorAgent.GetAgentCard());

        app.MapOpenAIResponses("light-agent");
        app.MapOpenAIResponses("music-agent");
        app.MapOpenAIResponses("orchestrator");

        app.MapAgentDiscovery("/agents");

        app.MapAgentDiscoveryEndpoint(taskManager, "/a2a/light-agent");
        app.MapAgentDiscoveryEndpoint(taskManager, "/a2a/music-agent");
        app.MapAgentDiscoveryEndpoint(taskManager, "/a2a/orchestrator");
    }

    public static void MapAgentDiscovery(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        var routeGroup = endpoints.MapGroup(path);
        routeGroup.MapGet("/", async (AgentRegistry agentCatalog, CancellationToken cancellationToken) =>
        {
            var results = new List<AgentCard>();
            await foreach (var result in agentCatalog.GetAgentsAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(result);
            }

            return Results.Ok(results);
        }).WithName("GetAgents");
    }

    public static IEndpointConventionBuilder MapAgentDiscoveryEndpoint(this IEndpointRouteBuilder endpoints, ITaskManager taskManager,
        [StringSyntax("Route")] string agentPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(taskManager);
        ArgumentException.ThrowIfNullOrEmpty(agentPath);

        var routeGroup = endpoints.MapGroup("");

        routeGroup.MapGet($"{agentPath}/.well-known/agent-card.json", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var agentUrl = $"{request.Scheme}://{request.Host}{agentPath}";
            var agentCard = await taskManager.OnAgentCardQuery(agentUrl, cancellationToken);
            return Results.Ok(agentCard);
        });

        return routeGroup;
    }
}