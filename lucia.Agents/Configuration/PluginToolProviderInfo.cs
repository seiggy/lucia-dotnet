using lucia.Agents.Mcp;

namespace lucia.Agents.Configuration;

/// <summary>Registered plugin capability and its selectable tools.</summary>
public sealed class PluginToolProviderInfo
{
    public required string ServerId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<McpToolInfo> Tools { get; init; } = [];
}
