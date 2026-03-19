using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Mcp;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed repository for agent definitions and MCP tool server configurations.
/// Manages the <c>agent_definitions</c> and <c>mcp_tool_servers</c> tables.
/// </summary>
public sealed class SqliteAgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public SqliteAgentDefinitionRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ── MCP Tool Servers ────────────────────────────────────────

    public async Task<List<McpToolServerDefinition>> GetAllToolServersAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM mcp_tool_servers ORDER BY name;";

        var servers = new List<McpToolServerDefinition>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var server = JsonSerializer.Deserialize<McpToolServerDefinition>(reader.GetString(0), JsonOptions);
            if (server is not null)
                servers.Add(server);
        }

        return servers;
    }

    public async Task<McpToolServerDefinition?> GetToolServerAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM mcp_tool_servers WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<McpToolServerDefinition>(json, JsonOptions);
    }

    public async Task UpsertToolServerAsync(McpToolServerDefinition server, CancellationToken ct = default)
    {
        server.UpdatedAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mcp_tool_servers (id, name, enabled, data)
            VALUES (@id, @name, @enabled, @data)
            ON CONFLICT(id) DO UPDATE SET name = @name, enabled = @enabled, data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", server.Id);
        cmd.Parameters.AddWithValue("@name", server.Name);
        cmd.Parameters.AddWithValue("@enabled", server.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(server, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteToolServerAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM mcp_tool_servers WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Agent Definitions ───────────────────────────────────────

    public async Task<List<AgentDefinition>> GetAllAgentDefinitionsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM agent_definitions ORDER BY name;";

        var definitions = new List<AgentDefinition>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var def = JsonSerializer.Deserialize<AgentDefinition>(reader.GetString(0), JsonOptions);
            if (def is not null)
                definitions.Add(def);
        }

        return definitions;
    }

    public async Task<List<AgentDefinition>> GetEnabledAgentDefinitionsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM agent_definitions WHERE enabled = 1 ORDER BY name;";

        var definitions = new List<AgentDefinition>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var def = JsonSerializer.Deserialize<AgentDefinition>(reader.GetString(0), JsonOptions);
            if (def is not null)
                definitions.Add(def);
        }

        return definitions;
    }

    public async Task<AgentDefinition?> GetAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM agent_definitions WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<AgentDefinition>(json, JsonOptions);
    }

    public async Task UpsertAgentDefinitionAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        definition.UpdatedAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_definitions (id, name, enabled, data)
            VALUES (@id, @name, @enabled, @data)
            ON CONFLICT(id) DO UPDATE SET name = @name, enabled = @enabled, data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", definition.Id);
        cmd.Parameters.AddWithValue("@name", definition.Name);
        cmd.Parameters.AddWithValue("@enabled", definition.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(definition, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM agent_definitions WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
