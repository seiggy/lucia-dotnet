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
        // A2A well-known agent card endpoint (serves this host's primary orchestrator card)
        api.MapGet("/.well-known/agent.json", GetLocalAgentCard)
            .WithName("A2A_WellKnownAgentCard")
            .WithSummary("Returns the local orchestrator's AgentCard per A2A spec")
            .WithDescription("Serves the primary agent card used by remote registries and clients.");
        return app;
    }

    private static async Task<Results<Ok<AgentCard>, ProblemHttpResult>> GetLocalAgentCard(
        [FromServices] AgentRegistry agentRegistry,
        CancellationToken cancellationToken = default)
    {
        // For now return the first registered agent that represents the orchestrator/light agent.
        // Later we may mark a specific AgentCard as primary.
        await foreach (var agent in agentRegistry.GetAgentsAsync(cancellationToken))
        {
            if (agent != null)
            {
                return TypedResults.Ok(agent);
            }
        }
        return TypedResults.Problem("No agents registered", statusCode: 404);
    }

    private static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> UnregisterAgent(
        [FromServices] AgentRegistry agentRegistry,
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
        [FromServices] AgentRegistry agentRegistry,
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

    public static async Task<IAsyncEnumerable<AgentCard>> GetAgents(
        [FromServices] AgentRegistry agentRegistry,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return agentRegistry.GetAgentsAsync(cancellationToken);
    }

    public static async Task<Results<Created, BadRequest<string>, ProblemHttpResult>> RegisterAgent(
        [FromServices] AgentRegistry agentRegistry,
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
