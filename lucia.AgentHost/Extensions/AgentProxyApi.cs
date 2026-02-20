using lucia.Agents.Registry;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Proxies A2A JSON-RPC messages from the dashboard to agent A2AHost instances.
/// The dashboard can't reach internal Aspire service URLs directly, so this
/// endpoint forwards the request server-side and returns the response.
/// </summary>
public static class AgentProxyApi
{
    public static IEndpointRouteBuilder MapAgentProxyApi(this WebApplication app)
    {
        app.MapPost("/agents/proxy", ProxyA2AMessageAsync)
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> ProxyA2AMessageAsync(
        [FromServices] IAgentRegistry agentRegistry,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromQuery] string agentUrl,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentUrl))
        {
            return TypedResults.BadRequest("agentUrl query parameter is required.");
        }

        // Verify the agent is registered
        var agent = await agentRegistry.GetAgentAsync(agentUrl, cancellationToken);
        if (agent is null)
        {
            return TypedResults.NotFound($"Agent not found: {agentUrl}");
        }

        // Read the raw JSON-RPC body from the incoming request
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        // Forward to the actual A2A agent endpoint
        // Agent cards may contain 0.0.0.0 (bind-all address) which isn't routable;
        // rewrite to localhost so the proxy can actually reach the agent.
        var client = httpClientFactory.CreateClient("AgentProxy");
        var rewrittenUrl = agentUrl
            .Replace("://0.0.0.0:", "://localhost:")
            .Replace("://[::0]:", "://localhost:")
            .Replace("://[::]:", "://localhost:");
        var targetUri = new Uri(rewrittenUrl);

        using var forwardRequest = new HttpRequestMessage(HttpMethod.Post, targetUri)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(forwardRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return TypedResults.Content(
            responseBody,
            "application/json",
            statusCode: (int)response.StatusCode);
    }
}
