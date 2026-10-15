namespace lucia.Agents.Abstractions;

/// <summary>
/// Reloads dynamic agent definitions and re-registers the resulting agents.
/// Callers use this after the set of available tools changes (e.g. an MCP server
/// reconnect) so rebuilt agents resolve tool wrappers against fresh clients.
/// </summary>
public interface IDynamicAgentLoader
{
    Task ReloadAsync(CancellationToken ct = default);
}
