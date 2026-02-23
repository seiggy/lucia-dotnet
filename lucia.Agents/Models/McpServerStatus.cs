namespace lucia.Agents.Models;

/// <summary>
/// Connection status for an MCP tool server.
/// </summary>
public sealed class McpServerStatus
{
    public required string ServerId { get; init; }
    public required string ServerName { get; init; }
    public required McpConnectionState State { get; init; }
    public string? ErrorMessage { get; init; }
    public int ToolCount { get; init; }
    public DateTime? ConnectedAt { get; init; }
}