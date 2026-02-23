using lucia.Agents.Configuration;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Repository for managing agent definitions and MCP tool server configurations.
/// </summary>
public interface IAgentDefinitionRepository
{
    // MCP Tool Servers
    Task<List<McpToolServerDefinition>> GetAllToolServersAsync(CancellationToken ct = default);
    Task<McpToolServerDefinition?> GetToolServerAsync(string id, CancellationToken ct = default);
    Task UpsertToolServerAsync(McpToolServerDefinition server, CancellationToken ct = default);
    Task DeleteToolServerAsync(string id, CancellationToken ct = default);

    // Agent Definitions
    Task<List<AgentDefinition>> GetAllAgentDefinitionsAsync(CancellationToken ct = default);
    Task<List<AgentDefinition>> GetEnabledAgentDefinitionsAsync(CancellationToken ct = default);
    Task<AgentDefinition?> GetAgentDefinitionAsync(string id, CancellationToken ct = default);
    Task UpsertAgentDefinitionAsync(AgentDefinition definition, CancellationToken ct = default);
    Task DeleteAgentDefinitionAsync(string id, CancellationToken ct = default);
}
