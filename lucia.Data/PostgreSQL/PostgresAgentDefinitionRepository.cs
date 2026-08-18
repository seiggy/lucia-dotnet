using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Mcp;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed repository for agent definitions and MCP tool server configurations.
/// Manages the <c>agent_definitions</c> and <c>mcp_tool_servers</c> tables.
/// </summary>
public sealed class PostgresAgentDefinitionRepository : IAgentDefinitionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresAgentDefinitionRepository([FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<McpToolServerDefinition>> GetAllToolServersAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM mcp_tool_servers ORDER BY name;";

        var servers = new List<McpToolServerDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var server = JsonSerializer.Deserialize<McpToolServerDefinition>(reader.GetString(0), JsonOptions);
            if (server is not null)
            {
                servers.Add(server);
            }
        }

        return servers;
    }

    public async Task<McpToolServerDefinition?> GetToolServerAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM mcp_tool_servers WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<McpToolServerDefinition>(json, JsonOptions) : null;
    }

    public async Task UpsertToolServerAsync(McpToolServerDefinition server, CancellationToken ct = default)
    {
        server.UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(server, JsonOptions);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mcp_tool_servers (id, name, enabled, data)
            VALUES (@id, @name, @enabled, @data)
            ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, enabled = EXCLUDED.enabled, data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", server.Id);
        cmd.Parameters.AddWithValue("name", server.Name);
        cmd.Parameters.AddWithValue("enabled", server.Enabled);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = json });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteToolServerAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM mcp_tool_servers WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<AgentDefinition>> GetAllAgentDefinitionsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM agent_definitions ORDER BY name;";

        var definitions = new List<AgentDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var definition = JsonSerializer.Deserialize<AgentDefinition>(reader.GetString(0), JsonOptions);
            if (definition is not null)
            {
                definitions.Add(definition);
            }
        }

        return definitions;
    }

    public async Task<List<AgentDefinition>> GetEnabledAgentDefinitionsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM agent_definitions WHERE enabled = TRUE ORDER BY name;";

        var definitions = new List<AgentDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var definition = JsonSerializer.Deserialize<AgentDefinition>(reader.GetString(0), JsonOptions);
            if (definition is not null)
            {
                definitions.Add(definition);
            }
        }

        return definitions;
    }

    public async Task<AgentDefinition?> GetAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM agent_definitions WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<AgentDefinition>(json, JsonOptions) : null;
    }

    public async Task UpsertAgentDefinitionAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(definition, JsonOptions);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO agent_definitions (id, name, enabled, data)
            VALUES (
                @id,
                @name,
                @enabled,
                jsonb_set(
                    @data,
                    '{updatedAt}',
                    '"1970-01-01T00:00:00.000000Z"'::jsonb))
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                enabled = EXCLUDED.enabled,
                data = jsonb_set(
                    EXCLUDED.data,
                    '{updatedAt}',
                    COALESCE(
                        agent_definitions.data->'updatedAt',
                        '"1970-01-01T00:00:00.000000Z"'::jsonb));
            """;
        upsert.Parameters.AddWithValue("id", definition.Id);
        upsert.Parameters.AddWithValue("name", definition.Name);
        upsert.Parameters.AddWithValue("enabled", definition.Enabled);
        upsert.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = json });
        await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var advanceMarker = connection.CreateCommand();
        advanceMarker.Transaction = transaction;
        advanceMarker.CommandText = """
            UPDATE agent_definitions
            SET data = jsonb_set(
                data,
                '{updatedAt}',
                to_jsonb(to_char(
                    GREATEST(
                        clock_timestamp(),
                        COALESCE(
                            (data->>'updatedAt')::timestamptz + INTERVAL '1 microsecond',
                            '-infinity'::timestamptz))
                        AT TIME ZONE 'UTC',
                    'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')))
            WHERE id = @id
            RETURNING data->>'updatedAt';
            """;
        advanceMarker.Parameters.AddWithValue("id", definition.Id);

        var persistedMarker = (string)(await advanceMarker.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        definition.UpdatedAt = DateTimeOffset.Parse(
            persistedMarker,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).UtcDateTime;
    }

    public async Task DeleteAgentDefinitionAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM agent_definitions WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
