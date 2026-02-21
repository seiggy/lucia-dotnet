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
    // Only allow proxying to loopback hosts — all A2A agents are local Aspire services
    private static readonly HashSet<string> s_allowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "127.0.0.1", "[::1]"
    };

    public static IEndpointRouteBuilder MapAgentProxyApi(this WebApplication app)
    {
        app.MapPost("/agents/proxy", ProxyA2AMessageAsync)
            .DisableAntiforgery()
            .RequireAuthorization();

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

        // Resolve relative URLs against the current server's address
        var resolvedUrl = agentUrl;
        if (!agentUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !agentUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = $"{request.Scheme}://{request.Host}";
            resolvedUrl = agentUrl.StartsWith('/')
                ? $"{baseUrl}{agentUrl}"
                : $"{baseUrl}/{agentUrl}";
        }

        // Verify the agent is registered (try both original and resolved URLs)
        var agent = await agentRegistry.GetAgentAsync(agentUrl, cancellationToken)
            ?? await agentRegistry.GetAgentAsync(resolvedUrl, cancellationToken);
        if (agent is null)
        {
            return TypedResults.NotFound("Agent not found for the specified URL.");
        }

        // Rewrite bind-all addresses to loopback and validate the resulting URI
        if (!TryRewriteAgentUrl(resolvedUrl, out var targetUri))
        {
            return TypedResults.BadRequest("Invalid or disallowed agent URL.");
        }

        // Read the raw JSON-RPC body from the incoming request
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        // Forward to the actual A2A agent endpoint
        var client = httpClientFactory.CreateClient("AgentProxy");

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

    /// <summary>
    /// Rewrites bind-all addresses to localhost and validates the URI is safe to proxy to.
    /// Only loopback hosts with http/https schemes are allowed to prevent SSRF.
    /// </summary>
    private static bool TryRewriteAgentUrl(string agentUrl, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? targetUri)
    {
        targetUri = null;

        var rewritten = agentUrl
            .Replace("://0.0.0.0:", "://localhost:")
            .Replace("://[::0]:", "://localhost:")
            .Replace("://[::]:", "://localhost:");

        if (!Uri.TryCreate(rewritten, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        if (!s_allowedHosts.Contains(uri.Host))
            return false;

        targetUri = uri;
        return true;
    }
}
