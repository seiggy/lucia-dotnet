using A2A;
using lucia.Agents.Registry;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

public static class AgentRegistryApi
{
    public static IEndpointRouteBuilder MapAgentRegistryApiV1(this WebApplication app)
    {
        app.MapGet("/agents", GetAgentsAsync);
        app.MapPost("/agents/register", RegisterAgentAsync)
            .DisableAntiforgery();
        app.MapPut("/agents/{agentId}", UpdateAgentAsync);
        app.MapDelete("/agents/{agentId}", UnregisterAgentAsync)
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IAsyncEnumerable<AgentCard>> GetAgentsAsync(
        [FromServices] IAgentRegistry agentRegistry,
        CancellationToken cancellationToken = default)
    {
        return agentRegistry.GetEnumerableAgentsAsync(cancellationToken);
    }

    private static async Task<Results<
            Created,
            BadRequest<string>,
            ProblemHttpResult
        >> RegisterAgentAsync(
        [FromServices] IAgentRegistry agentRegistry,
        [FromForm] string agentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        // Download the Agent Card for the provided URI
        var agentUri = new Uri(agentId);
        var resolver = new A2ACardResolver(agentUri);

        var agentCard = await resolver.GetAgentCardAsync(cancellationToken);

        if (agentCard == null)
        {
            return TypedResults.Problem($"Could not retrieve agent card for agent: {agentId}");
        }

        await agentRegistry.RegisterAgentAsync(agentCard, cancellationToken);

        return TypedResults.Created();
    }

    public static async Task<Results<
            Ok,
            BadRequest<string>,
            ProblemHttpResult
        >> UpdateAgentAsync(
            [FromServices] IAgentRegistry agentRegistry,
            [FromRoute] string agentId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        var agent = await agentRegistry.GetAgentAsync(agentId, cancellationToken);

        if (agent == null)
        {
            return TypedResults.Problem($"Agent not found: {agentId}", statusCode: 404);
        }

        // Download the Agent Card for the provided URI
        var agentUri = new Uri(agentId);
        var resolver = new A2ACardResolver(agentUri);

        var agentCard = await resolver.GetAgentCardAsync(cancellationToken);

        if (agentCard == null)
        {
            return TypedResults.Problem($"Could not retrieve agent card for agent: {agentId}");
        }

        await agentRegistry.RegisterAgentAsync(agentCard, cancellationToken);
        return TypedResults.Ok();
    }

    public static async Task<Results<
            Ok,
            BadRequest<string>,
            ProblemHttpResult
        >> UnregisterAgentAsync(
        [FromServices] IAgentRegistry agentRegistry,
        [FromRoute] string agentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        await agentRegistry.UnregisterAgentAsync(agentId, cancellationToken);
        return TypedResults.Ok();
    }

}
