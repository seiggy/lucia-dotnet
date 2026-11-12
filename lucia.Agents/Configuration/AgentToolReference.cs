using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.Agents.Configuration;

/// <summary>
/// References a specific tool from an MCP server or registered plugin provider.
/// Used in <see cref="AgentDefinition"/> to select individual tools per agent.
/// </summary>
public sealed class AgentToolReference
{
    /// <summary>
    /// The MCP server ID, or <c>plugin:{provider.Name}</c> for a plugin tool.
    /// </summary>
    public string ServerId { get; set; } = default!;

    /// <summary>
    /// The tool name reported by the MCP server or plugin provider.
    /// </summary>
    public string ToolName { get; set; } = default!;

    /// <summary>
    /// Set to <c>plugin</c> for plugin tools. Missing values identify MCP tools.
    /// </summary>
    public string? Source { get; set; }
}
