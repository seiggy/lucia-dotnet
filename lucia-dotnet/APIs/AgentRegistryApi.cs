using A2A;
using lucia.Agents.Registry;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia_dotnet.APIs;

public static class AgentRegistryApi
{
    public static IEndpointRouteBuilder MapAgentRegistryApiV1(this IEndpointRouteBuilder app)
    {
        var vApi = app.NewVersionedApi("Agents");

        var api = vApi.MapGroup("api").HasApiVersion(1, 0);

        api.MapGet("/agents", GetAgents);
        api.MapPost("/agents", RegisterAgent);
        api.MapPut("/agents/{agentUri}", UpdateAgent);
        api.MapDelete("/agents/{agentUri}", UnregisterAgent);
        return app;
    }

    private static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> UnregisterAgent(
        [FromServices] IAgentRegistry agentRegistry,
        [FromRoute] string agentUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentUri))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        // unregister the agent from the registry
        await agentRegistry.UnregisterAgentAsync(agentUri, cancellationToken);
        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> UpdateAgent(
        [FromServices] IAgentRegistry agentRegistry,
        [FromServices] IA2AClientService a2aService,
        [FromRoute] string agentUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentUri))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }
        // get the agent from the registry
        var agent = await agentRegistry.GetAgentAsync(agentUri, cancellationToken);
        if (agent == null)
        {
            return TypedResults.Problem(detail: $"Agent not found: {agentUri}", statusCode: 404);
        }
        // downlaod the latest agent from the provided URI
        var updatedAgent = await a2aService.DownloadAgentCardAsync(agentUri, cancellationToken);
        if (updatedAgent == null)
        {
            return TypedResults.Problem(detail: $"Agent not found at URI: {agentUri}", statusCode: 404);
        }
        // update the agent in the registry
        await agentRegistry.RegisterAgentAsync(updatedAgent, cancellationToken);
        return TypedResults.Ok();
    }

    public static async Task<IReadOnlyCollection<AgentCard>> GetAgents(
        [FromServices] IAgentRegistry agentRegistry,
        CancellationToken cancellationToken = default)
    {
        return await agentRegistry.GetAgentsAsync(cancellationToken);
    }
    
    public static async Task<Results<Created, BadRequest<string>, ProblemHttpResult>> RegisterAgent(
        [FromServices] IAgentRegistry agentRegistry,
        [FromServices] IA2AClientService a2aService,
        [FromForm] string agentUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentUri))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }
        
        // download the agent from the provided URI
        var agent = await a2aService.DownloadAgentCardAsync(agentUri, cancellationToken);
        if (agent == null)
        {
            return TypedResults.Problem(detail: $"Agent not found at URI: {agentUri}", statusCode: 404);
        }
        // register the agent with the registry
        await agentRegistry.RegisterAgentAsync(agent, cancellationToken);
        return TypedResults.Created(agentUri);
    }
}