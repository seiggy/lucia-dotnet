namespace lucia.Agents.Configuration;

/// <summary>
/// References a specific tool from an MCP server.
/// Used in <see cref="AgentDefinition"/> to select individual tools per agent.
/// </summary>
public sealed class AgentToolReference
{
    /// <summary>
    /// The MCP server ID this tool belongs to.
    /// </summary>
    public string ServerId { get; set; } = default!;

    /// <summary>
    /// The tool name as reported by the MCP server's ListToolsAsync().
    /// </summary>
    public string ToolName { get; set; } = default!;
}
