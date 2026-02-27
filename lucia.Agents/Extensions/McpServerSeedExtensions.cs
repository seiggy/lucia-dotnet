using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Seeds MetaMCP and other MCP servers from environment configuration
/// when METAMCP_URL (or similar) is set. Enables headless/Docker deployments
/// to register MetaMCP without using the dashboard.
/// </summary>
public static class McpServerSeedExtensions
{
    private const string MetaMcpId = "metamcp";
    private const string MetaMcpName = "MetaMCP";

    /// <summary>
    /// Seeds MetaMCP server definition when METAMCP_URL is configured.
    /// Skips if a server with Id "metamcp" already exists.
    /// METAMCP_URL should be the full SSE endpoint (e.g. http://host:12008/metamcp/openwebui-api/sse).
    /// METAMCP_API_KEY (optional) is sent as Authorization: Bearer header.
    /// </summary>
    public static async Task SeedMetaMcpFromConfigAsync(
        this IAgentDefinitionRepository repository,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        var url = configuration["METAMCP_URL"];
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogDebug("METAMCP_URL not set — skipping MetaMCP seed.");
            return;
        }

        url = url.Trim();

        // Ensure URL includes path for MetaMCP openwebui-api SSE endpoint
        var baseUri = url.Contains("/metamcp/", StringComparison.OrdinalIgnoreCase)
            ? url
            : url.TrimEnd('/') + "/metamcp/openwebui-api/sse";

        var existing = await repository.GetToolServerAsync(MetaMcpId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            logger.LogDebug("MetaMCP server '{Id}' already exists — skipping seed.", MetaMcpId);
            return;
        }

        var apiKey = configuration["METAMCP_API_KEY"]?.Trim();
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(apiKey))
        {
            headers["Authorization"] = "Bearer " + apiKey;
        }

        var server = new McpToolServerDefinition
        {
            Id = MetaMcpId,
            Name = MetaMcpName,
            Description = "MetaMCP Open Web UI API — Calculator, YouTube, Time, Sequential-Thinking, and more.",
            TransportType = "sse",
            Url = baseUri,
            Headers = headers,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await repository.UpsertToolServerAsync(server, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Seeded MetaMCP server '{Id}' ({Url}, apiKey={HasKey})",
            MetaMcpId, baseUri, !string.IsNullOrEmpty(apiKey));
    }
}
