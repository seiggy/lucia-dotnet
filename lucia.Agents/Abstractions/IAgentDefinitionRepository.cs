using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Mcp;

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

    /// <summary>
    /// Atomically inserts a new definition only when no row with the same Id exists.
    /// Returns <c>false</c> (and writes nothing) when the Id was already in use.
    /// </summary>
    Task<bool> InsertAgentDefinitionIfAbsentAsync(AgentDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Atomically renames a definition's Id (Id and Name both move to <paramref name="newId"/>).
    /// The rename only happens when the old row still exists AND the target id is free, so
    /// concurrent callers can never clobber an existing definition. Returns <c>false</c> (and
    /// writes nothing) when either condition does not hold.
    /// </summary>
    Task<bool> RenameAgentDefinitionIfTargetFreeAsync(string oldId, string newId, CancellationToken ct = default);
    Task DeleteAgentDefinitionAsync(string id, CancellationToken ct = default);
}
