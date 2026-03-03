namespace lucia.Agents.Mcp;

/// <summary>
/// Describes a tool available from an MCP server (for the UI catalog).
/// </summary>
public sealed class McpToolInfo
{
    public required string ServerId { get; init; }
    public required string ServerName { get; init; }
    public required string ToolName { get; init; }
    public string? Description { get; init; }
}
