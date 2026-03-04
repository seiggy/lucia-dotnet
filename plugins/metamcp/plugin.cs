using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class MetaMcpPlugin : ILuciaPlugin
{
    public string PluginId => "metamcp";

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MetaMcpPlugin");
        logger.LogInformation("MetaMCP plugin executing.");

        var config = services.GetRequiredService<IConfiguration>();
        var url = config["METAMCP_URL"];
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogInformation("METAMCP_URL not configured — skipping MetaMCP seed.");
            return;
        }

        url = url.Trim();
        var baseUri = url.Contains("/metamcp/", StringComparison.OrdinalIgnoreCase)
            ? url
            : url.TrimEnd('/') + "/metamcp/openwebui-api/sse";

        var repo = services.GetRequiredService<IAgentDefinitionRepository>();
        var existing = await repo.GetToolServerAsync("metamcp", cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("MetaMCP server already registered — no changes needed.");
            return;
        }

        var apiKey = config["METAMCP_API_KEY"]?.Trim();
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(apiKey))
            headers["Authorization"] = "Bearer " + apiKey;

        await repo.UpsertToolServerAsync(new McpToolServerDefinition
        {
            Id = "metamcp",
            Name = "MetaMCP",
            Description = "MetaMCP Open Web UI API — Calculator, YouTube, Time, Sequential-Thinking, and more.",
            TransportType = "sse",
            Url = baseUri,
            Headers = headers,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, cancellationToken);

        logger.LogInformation("Seeded MetaMCP server ({Url}).", baseUri);
    }
}

new MetaMcpPlugin()
