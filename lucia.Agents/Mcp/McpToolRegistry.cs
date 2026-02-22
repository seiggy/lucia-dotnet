using System.Collections.Concurrent;
using lucia.Agents.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace lucia.Agents.Mcp;

/// <summary>
/// Manages MCP client connections and exposes a tool catalog for dynamic agents.
/// Connects to MCP servers defined in MongoDB, caches clients, and resolves
/// individual tools by serverId + toolName.
/// </summary>
public sealed class McpToolRegistry : IMcpToolRegistry
{
    private readonly IAgentDefinitionRepository _repository;
    private readonly ILogger<McpToolRegistry> _logger;
    private readonly ConcurrentDictionary<string, McpClientEntry> _clients = new();
    private readonly ConcurrentDictionary<string, McpServerStatus> _statuses = new();

    public McpToolRegistry(
        IAgentDefinitionRepository repository,
        ILogger<McpToolRegistry> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var servers = await _repository.GetAllToolServersAsync(ct);
        foreach (var server in servers.Where(s => s.Enabled))
        {
            try
            {
                await ConnectServerAsync(server.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect MCP server {ServerId} during initialization", server.Id);
            }
        }
    }

    public async Task ConnectServerAsync(string serverId, CancellationToken ct = default)
    {
        var server = await _repository.GetToolServerAsync(serverId, ct);
        if (server is null)
        {
            _logger.LogWarning("MCP server {ServerId} not found in repository", serverId);
            return;
        }

        // Disconnect existing client if reconnecting
        await DisconnectServerAsync(serverId, ct);

        _statuses[serverId] = new McpServerStatus
        {
            ServerId = serverId,
            ServerName = server.Name,
            State = McpConnectionState.Connecting
        };

        try
        {
            var transport = CreateTransport(server);
            var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            var tools = await client.ListToolsAsync(cancellationToken: ct);

            _clients[serverId] = new McpClientEntry(server, client, tools.ToList());
            _statuses[serverId] = new McpServerStatus
            {
                ServerId = serverId,
                ServerName = server.Name,
                State = McpConnectionState.Connected,
                ToolCount = tools.Count,
                ConnectedAt = DateTime.UtcNow
            };

            _logger.LogInformation(
                "Connected MCP server {ServerId} ({ServerName}) with {ToolCount} tools",
                serverId, server.Name, tools.Count);
        }
        catch (Exception ex)
        {
            _statuses[serverId] = new McpServerStatus
            {
                ServerId = serverId,
                ServerName = server.Name,
                State = McpConnectionState.Error,
                ErrorMessage = ex.Message
            };

            _logger.LogError(ex, "Failed to connect MCP server {ServerId}", serverId);
            throw;
        }
    }

    public async Task DisconnectServerAsync(string serverId, CancellationToken ct = default)
    {
        if (_clients.TryRemove(serverId, out var entry))
        {
            try
            {
                await entry.Client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MCP client for {ServerId}", serverId);
            }
        }

        if (_statuses.TryGetValue(serverId, out var status))
        {
            _statuses[serverId] = new McpServerStatus
            {
                ServerId = serverId,
                ServerName = status.ServerName,
                State = McpConnectionState.Disconnected
            };
        }
    }

    public Task<IReadOnlyList<McpToolInfo>> GetAvailableToolsAsync(string serverId, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(serverId, out var entry))
            return Task.FromResult<IReadOnlyList<McpToolInfo>>([]);

        var tools = entry.Tools.Select(t => new McpToolInfo
        {
            ServerId = serverId,
            ServerName = entry.Server.Name,
            ToolName = t.Name,
            Description = t.Description
        }).ToList();

        return Task.FromResult<IReadOnlyList<McpToolInfo>>(tools);
    }

    public Task<IReadOnlyList<McpToolInfo>> GetAllAvailableToolsAsync(CancellationToken ct = default)
    {
        var allTools = _clients.SelectMany(kvp =>
            kvp.Value.Tools.Select(t => new McpToolInfo
            {
                ServerId = kvp.Key,
                ServerName = kvp.Value.Server.Name,
                ToolName = t.Name,
                Description = t.Description
            })).ToList();

        return Task.FromResult<IReadOnlyList<McpToolInfo>>(allTools);
    }

    public Task<IReadOnlyList<AITool>> ResolveToolsAsync(
        IReadOnlyList<AgentToolReference> toolReferences,
        CancellationToken ct = default)
    {
        var resolved = new List<AITool>();

        foreach (var reference in toolReferences)
        {
            if (!_clients.TryGetValue(reference.ServerId, out var entry))
            {
                _logger.LogWarning(
                    "MCP server {ServerId} not connected, skipping tool {ToolName}",
                    reference.ServerId, reference.ToolName);
                continue;
            }

            var tool = entry.Tools.FirstOrDefault(t =>
                string.Equals(t.Name, reference.ToolName, StringComparison.OrdinalIgnoreCase));

            if (tool is null)
            {
                _logger.LogWarning(
                    "Tool {ToolName} not found on MCP server {ServerId}",
                    reference.ToolName, reference.ServerId);
                continue;
            }

            resolved.Add(tool);
        }

        return Task.FromResult<IReadOnlyList<AITool>>(resolved);
    }

    public IReadOnlyDictionary<string, McpServerStatus> GetServerStatuses()
    {
        return _statuses;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _clients)
        {
            try
            {
                await kvp.Value.Client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing MCP client for {ServerId}", kvp.Key);
            }
        }

        _clients.Clear();
        _statuses.Clear();
    }

    private static IClientTransport CreateTransport(McpToolServerDefinition server)
    {
        return server.TransportType.ToLowerInvariant() switch
        {
            "stdio" => CreateStdioTransport(server),
            "http" or "sse" => CreateHttpTransport(server),
            _ => throw new NotSupportedException($"Transport type '{server.TransportType}' is not supported")
        };
    }

    private static StdioClientTransport CreateStdioTransport(McpToolServerDefinition server)
    {
        if (string.IsNullOrWhiteSpace(server.Command))
            throw new InvalidOperationException($"MCP server '{server.Id}' uses stdio transport but has no command configured");

        var options = new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = [.. server.Arguments],
        };

        if (server.EnvironmentVariables.Count > 0)
        {
            options.EnvironmentVariables = new Dictionary<string, string?>(
                server.EnvironmentVariables.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)));
        }

        if (!string.IsNullOrWhiteSpace(server.WorkingDirectory))
        {
            options.WorkingDirectory = server.WorkingDirectory;
        }

        return new StdioClientTransport(options);
    }

    private static HttpClientTransport CreateHttpTransport(McpToolServerDefinition server)
    {
        if (string.IsNullOrWhiteSpace(server.Url))
            throw new InvalidOperationException($"MCP server '{server.Id}' uses HTTP/SSE transport but has no URL configured");

        return new HttpClientTransport(new HttpClientTransportOptions
        {
            Name = server.Name,
            Endpoint = new Uri(server.Url),
        });
    }

    private sealed record McpClientEntry(
        McpToolServerDefinition Server,
        McpClient Client,
        List<McpClientTool> Tools);
}
