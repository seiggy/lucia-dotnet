using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class MetaMcpPlugin : ILuciaPlugin
{
    public string PluginId => "metamcp";

    public Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task OnSystemReadyAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await SeedMetaMcpAsync(services, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedMetaMcpAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MetaMcpPlugin");
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
        var existing = await repo.GetToolServerAsync("metamcp", cancellationToken).ConfigureAwait(false);
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
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Seeded MetaMCP server ({Url}).", baseUri);
    }
}

new MetaMcpPlugin()
