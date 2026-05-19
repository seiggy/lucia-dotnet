using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IConfigStoreWriter"/>.
/// Stores configuration entries as individual columns in the <c>configuration</c> table.
/// </summary>
public sealed class PostgresConfigStoreWriter : IConfigStoreWriter
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresConfigStoreWriter([FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SetAsync(
        string key,
        string? value,
        string updatedBy = "setup-wizard",
        bool isSensitive = false,
        CancellationToken cancellationToken = default)
    {
        var section = key.Contains(':') ? key[..key.LastIndexOf(':')] : key;

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO configuration (key, value, section, updated_at, updated_by, is_sensitive)
            VALUES (@key, @value, @section, @updatedAt, @updatedBy, @isSensitive)
            ON CONFLICT (key) DO UPDATE SET
                value = EXCLUDED.value,
                section = EXCLUDED.section,
                updated_at = EXCLUDED.updated_at,
                updated_by = EXCLUDED.updated_by,
                is_sensitive = EXCLUDED.is_sensitive;
            """;
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", (object?)value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("section", section);
        cmd.Parameters.AddWithValue("updatedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("updatedBy", updatedBy);
        cmd.Parameters.AddWithValue("isSensitive", isSensitive);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM configuration WHERE key = @key;";
        cmd.Parameters.AddWithValue("key", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is DBNull or null ? null : (string)result;
    }

    public async Task<long> GetEntryCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM configuration;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    public async Task<IReadOnlySet<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key FROM configuration;";

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task InsertManyAsync(IReadOnlyList<ConfigEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO configuration (key, value, section, updated_at, updated_by, is_sensitive)
                VALUES (@key, @value, @section, @updatedAt, @updatedBy, @isSensitive);
                """;
            cmd.Parameters.AddWithValue("key", entry.Key);
            cmd.Parameters.AddWithValue("value", (object?)entry.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("section", entry.Section);
            cmd.Parameters.AddWithValue("updatedAt", entry.UpdatedAt);
            cmd.Parameters.AddWithValue("updatedBy", entry.UpdatedBy);
            cmd.Parameters.AddWithValue("isSensitive", entry.IsSensitive);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfigEntry>> GetAllEntriesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key, value, section, updated_at, updated_by, is_sensitive FROM configuration;";

        return await ReadEntriesAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfigEntry>> GetEntriesBySectionAsync(string section, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key, value, section, updated_at, updated_by, is_sensitive FROM configuration WHERE section = @section;";
        cmd.Parameters.AddWithValue("section", section);

        return await ReadEntriesAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfigEntry>> GetEntriesByKeyPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key, value, section, updated_at, updated_by, is_sensitive FROM configuration WHERE key LIKE @prefix;";
        cmd.Parameters.AddWithValue("prefix", keyPrefix + "%");

        return await ReadEntriesAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM configuration;";

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> DeleteByKeyPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM configuration WHERE key LIKE @prefix;";
        cmd.Parameters.AddWithValue("prefix", keyPrefix + "%");

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ConfigEntry>> ReadEntriesAsync(NpgsqlCommand cmd, CancellationToken cancellationToken)
    {
        var entries = new List<ConfigEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new ConfigEntry
            {
                Key = reader.GetString(0),
                Value = reader.IsDBNull(1) ? null : reader.GetString(1),
                Section = reader.GetString(2),
                UpdatedAt = reader.GetFieldValue<DateTime>(3),
                UpdatedBy = reader.GetString(4),
                IsSensitive = reader.GetBoolean(5),
            });
        }

        return entries;
    }
}
