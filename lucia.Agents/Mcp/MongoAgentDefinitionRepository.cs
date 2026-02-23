using lucia.Agents.Configuration;
using MongoDB.Driver;

namespace lucia.Agents.Mcp;

/// <summary>
/// MongoDB implementation of <see cref="IAgentDefinitionRepository"/>.
/// Manages both agent_definitions and mcp_tool_servers collections in luciaconfig.
/// </summary>
public sealed class MongoAgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly IMongoCollection<McpToolServerDefinition> _toolServers;
    private readonly IMongoCollection<AgentDefinition> _agentDefinitions;

    public MongoAgentDefinitionRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(ConfigEntry.DatabaseName);
        _toolServers = db.GetCollection<McpToolServerDefinition>(McpToolServerDefinition.CollectionName);
        _agentDefinitions = db.GetCollection<AgentDefinition>(AgentDefinition.CollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _toolServers.Indexes.CreateMany([
            new CreateIndexModel<McpToolServerDefinition>(
                Builders<McpToolServerDefinition>.IndexKeys.Ascending(s => s.Name),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<McpToolServerDefinition>(
                Builders<McpToolServerDefinition>.IndexKeys.Ascending(s => s.Enabled)),
        ]);

        _agentDefinitions.Indexes.CreateMany([
            new CreateIndexModel<AgentDefinition>(
                Builders<AgentDefinition>.IndexKeys.Ascending(a => a.Name),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<AgentDefinition>(
                Builders<AgentDefinition>.IndexKeys.Ascending(a => a.Enabled)),
        ]);
    }

    // MCP Tool Servers

    public async Task<List<McpToolServerDefinition>> GetAllToolServersAsync(CancellationToken ct = default)
    {
        return await _toolServers.Find(_ => true)
            .SortBy(s => s.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<McpToolServerDefinition?> GetToolServerAsync(string id, CancellationToken ct = default)
    {
        return await _toolServers.Find(s => s.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertToolServerAsync(McpToolServerDefinition server, CancellationToken ct = default)
    {
        server.UpdatedAt = DateTime.UtcNow;
        await _toolServers.ReplaceOneAsync(
            s => s.Id == server.Id,
            server,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    public async Task DeleteToolServerAsync(string id, CancellationToken ct = default)
    {
        await _toolServers.DeleteOneAsync(s => s.Id == id, ct).ConfigureAwait(false);
    }

    // Agent Definitions

    public async Task<List<AgentDefinition>> GetAllAgentDefinitionsAsync(CancellationToken ct = default)
    {
        return await _agentDefinitions.Find(_ => true)
            .SortBy(a => a.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AgentDefinition>> GetEnabledAgentDefinitionsAsync(CancellationToken ct = default)
    {
        return await _agentDefinitions.Find(a => a.Enabled)
            .SortBy(a => a.Name)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<AgentDefinition?> GetAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        return await _agentDefinitions.Find(a => a.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertAgentDefinitionAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        definition.UpdatedAt = DateTime.UtcNow;
        await _agentDefinitions.ReplaceOneAsync(
            a => a.Id == definition.Id,
            definition,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    public async Task DeleteAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        await _agentDefinitions.DeleteOneAsync(a => a.Id == id, ct).ConfigureAwait(false);
    }
}
