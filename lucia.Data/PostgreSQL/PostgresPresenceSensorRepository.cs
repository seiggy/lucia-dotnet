using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IPresenceSensorRepository"/>.
/// Manages the <c>presence_sensor_mappings</c> and <c>presence_config</c> tables.
/// </summary>
public sealed class PostgresPresenceSensorRepository : IPresenceSensorRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresPresenceSensorRepository([FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PresenceSensorMapping>> GetAllMappingsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM presence_sensor_mappings;";

        var mappings = new List<PresenceSensorMapping>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var mapping = JsonSerializer.Deserialize<PresenceSensorMapping>(reader.GetString(0), JsonOptions);
            if (mapping is not null)
            {
                mappings.Add(mapping);
            }
        }

        return mappings;
    }

    public async Task ReplaceAutoDetectedMappingsAsync(IReadOnlyList<PresenceSensorMapping> autoDetected, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM presence_sensor_mappings WHERE is_user_override = FALSE;";
            await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (autoDetected.Count > 0)
        {
            var reservedEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var selectCmd = connection.CreateCommand())
            {
                selectCmd.Transaction = transaction;
                selectCmd.CommandText = "SELECT id FROM presence_sensor_mappings;";
                await using var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    reservedEntityIds.Add(reader.GetString(0));
                }
            }

            var filteredMappings = autoDetected
                .GroupBy(static mapping => mapping.EntityId, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.OrderByDescending(static mapping => mapping.Confidence).First())
                .Where(mapping => !reservedEntityIds.Contains(mapping.EntityId))
                .ToList();

            foreach (var mapping in filteredMappings)
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO presence_sensor_mappings (id, area_id, is_user_override, data)
                    VALUES (@id, @areaId, @isUserOverride, @data);
                    """;
                insertCmd.Parameters.AddWithValue("id", mapping.EntityId);
                insertCmd.Parameters.AddWithValue("areaId", (object?)mapping.AreaId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("isUserOverride", mapping.IsUserOverride);
                insertCmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(mapping, JsonOptions) });

                await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertMappingAsync(PresenceSensorMapping mapping, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO presence_sensor_mappings (id, area_id, is_user_override, data)
            VALUES (@id, @areaId, @isUserOverride, @data)
            ON CONFLICT (id) DO UPDATE SET
                area_id = EXCLUDED.area_id,
                is_user_override = EXCLUDED.is_user_override,
                data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", mapping.EntityId);
        cmd.Parameters.AddWithValue("areaId", (object?)mapping.AreaId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("isUserOverride", mapping.IsUserOverride);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(mapping, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteMappingAsync(string entityId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM presence_sensor_mappings WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", entityId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> GetEnabledAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM presence_config WHERE key = @key;";
        cmd.Parameters.AddWithValue("key", "presence_detection_enabled");

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string value)
        {
            return true;
        }

        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO presence_config (key, value)
            VALUES (@key, @value)
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
            """;
        cmd.Parameters.AddWithValue("key", "presence_detection_enabled");
        cmd.Parameters.AddWithValue("value", enabled.ToString().ToLowerInvariant());

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
