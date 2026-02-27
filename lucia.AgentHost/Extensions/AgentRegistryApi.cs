using A2A;
using lucia.Agents.Registry;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

public static class AgentRegistryApi
{
    public static IEndpointRouteBuilder MapAgentRegistryApiV1(this WebApplication app)
    {
        // Catalog discovery: allow anonymous so HA integration and external clients can fetch without API key
        app.MapGet("/agents", GetAgentsAsync)
            .AllowAnonymous();
        // Internal only: agents register themselves via platform-injected token
        app.MapPost("/agents/register", RegisterAgentAsync)
            .DisableAntiforgery()
            .RequireAuthorization("InternalOnly");
        // External only: dashboard/admin updates an agent card
        app.MapPut("/agents/{agentId}", UpdateAgentAsync)
            .RequireAuthorization();
        // Internal only: agents unregister on shutdown
        app.MapDelete("/agents/{agentId}", UnregisterAgentAsync)
            .DisableAntiforgery()
            .RequireAuthorization("InternalOnly");

        return app;
    }

    private static async Task<Ok<List<AgentCard>>> GetAgentsAsync(
        [FromServices] IAgentRegistry agentRegistry,
        CancellationToken cancellationToken = default)
    {
        var agents = await agentRegistry.GetAllAgentsAsync(cancellationToken).ConfigureAwait(false);
        // Return orchestrator first so the Home Assistant integration defaults to the full
        // assistant (routing to light, music, timer, etc.) instead of a single specialist.
        var list = agents
            .OrderBy(a => IsOrchestrator(a) ? 0 : 1)
            .ThenBy(a => a.Name, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(list);
    }

    private static bool IsOrchestrator(AgentCard card) =>
        string.Equals(card.Name, "orchestrator", StringComparison.OrdinalIgnoreCase)
        || (card.Url?.EndsWith("/agent", StringComparison.OrdinalIgnoreCase) == true);

    private static async Task<Results<
            Created,
            BadRequest<string>,
            ProblemHttpResult
        >> RegisterAgentAsync(
        [FromServices] IAgentRegistry agentRegistry,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILoggerFactory loggerFactory,
        [FromForm] string agentId,
        CancellationToken cancellationToken = default)
    {
        var logger = loggerFactory.CreateLogger("AgentRegistryApi");
        logger.LogInformation("Received agent registration request for {AgentId}", agentId);

        if (string.IsNullOrWhiteSpace(agentId))
        {
            logger.LogWarning("Agent registration rejected: empty agentId");
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        // Fetch the agent card using an HttpClient from DI so that
        // Aspire service discovery can resolve logical service names
        var agentUri = new Uri(agentId);
        AgentCard? agentCard = null;
        var httpClient = httpClientFactory.CreateClient("AgentProxy");
        const int maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                logger.LogInformation("Fetching agent card from {AgentUri} (attempt {Attempt}/{MaxRetries})",
                    agentUri, attempt, maxRetries);
                var resolver = new A2ACardResolver(agentUri, httpClient);
                agentCard = await resolver.GetAgentCardAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Successfully fetched agent card for {AgentName} from {AgentUri}",
                    agentCard?.Name ?? "unknown", agentUri);
                break;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(2 * attempt);
                logger.LogWarning(ex,
                    "Failed to fetch agent card from {AgentUri} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}s...",
                    agentUri, attempt, maxRetries, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to fetch agent card from {AgentUri} after {MaxRetries} attempts",
                    agentUri, maxRetries);
            }
        }

        if (agentCard == null)
        {
            logger.LogError("Could not retrieve agent card for {AgentId} after all retries", agentId);
            return TypedResults.Problem($"Could not retrieve agent card for agent: {agentId}");
        }

        await agentRegistry.RegisterAgentAsync(agentCard, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Agent {AgentName} registered successfully with URL {AgentUrl}",
            agentCard.Name, agentCard.Url);

        return TypedResults.Created();
    }

    public static async Task<Results<
            Ok,
            BadRequest<string>,
            ProblemHttpResult
        >> UpdateAgentAsync(
            [FromServices] IAgentRegistry agentRegistry,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromRoute] string agentId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return TypedResults.BadRequest("Agent URI must be provided");
        }

        var agent = await agentRegistry.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);

        if (agent == null)
        {
            return TypedResults.Problem($"Agent not found: {agentId}", statusCode: 404);
        }

        // Use DI HttpClient for service discovery resolution
        var agentUri = new Uri(agentId);
        var httpClient = httpClientFactory.CreateClient("AgentProxy");
        var resolver = new A2ACardResolver(agentUri, httpClient);

        var agentCard = await resolver.GetAgentCardAsync(cancellationToken).ConfigureAwait(false);

        await agentRegistry.RegisterAgentAsync(agentCard, cancellationToken).ConfigureAwait(false);
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

        await agentRegistry.UnregisterAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok();
    }

}
