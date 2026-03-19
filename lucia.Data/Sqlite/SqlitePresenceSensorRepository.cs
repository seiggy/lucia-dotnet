using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IPresenceSensorRepository"/>.
/// Manages the <c>presence_sensor_mappings</c> and <c>presence_config</c> tables.
/// </summary>
public sealed class SqlitePresenceSensorRepository : IPresenceSensorRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public SqlitePresenceSensorRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PresenceSensorMapping>> GetAllMappingsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM presence_sensor_mappings;";

        var mappings = new List<PresenceSensorMapping>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var mapping = JsonSerializer.Deserialize<PresenceSensorMapping>(reader.GetString(0), JsonOptions);
            if (mapping is not null)
                mappings.Add(mapping);
        }

        return mappings;
    }

    public async Task ReplaceAutoDetectedMappingsAsync(
        IReadOnlyList<PresenceSensorMapping> autoDetected,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        // Delete all non-user-override mappings
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM presence_sensor_mappings WHERE is_user_override = 0;";
            await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (autoDetected.Count > 0)
        {
            // Get remaining user-override entity IDs to avoid collisions
            var reservedEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var selectCmd = connection.CreateCommand())
            {
                selectCmd.Transaction = transaction;
                selectCmd.CommandText = "SELECT id FROM presence_sensor_mappings;";
                using var reader = await selectCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    reservedEntityIds.Add(reader.GetString(0));
                }
            }

            // Deduplicate by EntityId keeping highest confidence, exclude reserved
            var filteredMappings = autoDetected
                .GroupBy(m => m.EntityId, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group
                    .OrderByDescending(mapping => mapping.Confidence)
                    .First())
                .Where(mapping => !reservedEntityIds.Contains(mapping.EntityId))
                .ToList();

            foreach (var mapping in filteredMappings)
            {
                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO presence_sensor_mappings (id, area_id, is_user_override, data)
                    VALUES (@id, @areaId, @isUserOverride, @data);
                    """;
                insertCmd.Parameters.AddWithValue("@id", mapping.EntityId);
                insertCmd.Parameters.AddWithValue("@areaId", mapping.AreaId);
                insertCmd.Parameters.AddWithValue("@isUserOverride", mapping.IsUserOverride ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(mapping, JsonOptions));

                await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertMappingAsync(PresenceSensorMapping mapping, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO presence_sensor_mappings (id, area_id, is_user_override, data)
            VALUES (@id, @areaId, @isUserOverride, @data)
            ON CONFLICT(id) DO UPDATE SET
                area_id = @areaId,
                is_user_override = @isUserOverride,
                data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", mapping.EntityId);
        cmd.Parameters.AddWithValue("@areaId", mapping.AreaId);
        cmd.Parameters.AddWithValue("@isUserOverride", mapping.IsUserOverride ? 1 : 0);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(mapping, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteMappingAsync(string entityId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM presence_sensor_mappings WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", entityId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> GetEnabledAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM presence_config WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", "presence_detection_enabled");

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string value)
            return true; // enabled by default

        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO presence_config (key, value)
            VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = @value;
            """;
        cmd.Parameters.AddWithValue("@key", "presence_detection_enabled");
        cmd.Parameters.AddWithValue("@value", enabled.ToString().ToLowerInvariant());

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
