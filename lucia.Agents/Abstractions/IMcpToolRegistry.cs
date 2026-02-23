using lucia.Agents.Mcp;
using lucia.Agents.Models;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Platform-wide registry for MCP tool servers. Manages client connections
/// and provides tool lookup for dynamic agent construction.
/// </summary>
public interface IMcpToolRegistry : IAsyncDisposable
{
    /// <summary>
    /// Connects to all enabled MCP servers from the repository.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all available tools from a specific MCP server.
    /// </summary>
    Task<IReadOnlyList<McpToolInfo>> GetAvailableToolsAsync(string serverId, CancellationToken ct = default);

    /// <summary>
    /// Gets all tools from all connected servers (for the tool catalog UI).
    /// </summary>
    Task<IReadOnlyList<McpToolInfo>> GetAllAvailableToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves specific AITool instances for an agent's tool references.
    /// Returns only the tools that match the references and are available.
    /// </summary>
    Task<IReadOnlyList<AITool>> ResolveToolsAsync(
        IReadOnlyList<Configuration.AgentToolReference> toolReferences,
        CancellationToken ct = default);

    /// <summary>
    /// Connects (or reconnects) a specific MCP server by ID.
    /// </summary>
    Task ConnectServerAsync(string serverId, CancellationToken ct = default);

    /// <summary>
    /// Disconnects a specific MCP server.
    /// </summary>
    Task DisconnectServerAsync(string serverId, CancellationToken ct = default);

    /// <summary>
    /// Gets the connection status of all registered servers.
    /// </summary>
    IReadOnlyDictionary<string, McpServerStatus> GetServerStatuses();
}
