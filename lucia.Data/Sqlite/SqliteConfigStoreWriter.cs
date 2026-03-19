using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IConfigStoreWriter"/>.
/// Stores configuration entries as individual columns in the <c>configuration</c> table.
/// </summary>
public sealed class SqliteConfigStoreWriter : IConfigStoreWriter
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteConfigStoreWriter(SqliteConnectionFactory connectionFactory)
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
        var section = key.Contains(':') ? key[..key.IndexOf(':')] : key;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO configuration (key, value, section, updated_at, updated_by, is_sensitive)
            VALUES (@key, @value, @section, @updatedAt, @updatedBy, @isSensitive)
            ON CONFLICT(key) DO UPDATE SET
                value = @value,
                section = @section,
                updated_at = @updatedAt,
                updated_by = @updatedBy,
                is_sensitive = @isSensitive;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@section", section);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedBy", updatedBy);
        cmd.Parameters.AddWithValue("@isSensitive", isSensitive ? 1 : 0);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM configuration WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is DBNull or null ? null : (string)result;
    }

    public async Task<long> GetEntryCountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM configuration;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count ? count : 0;
    }

    public async Task<IReadOnlySet<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key FROM configuration;";

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }

    public async Task InsertManyAsync(IReadOnlyList<ConfigEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return;

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var entry in entries)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO configuration (key, value, section, updated_at, updated_by, is_sensitive)
                VALUES (@key, @value, @section, @updatedAt, @updatedBy, @isSensitive);
                """;
            cmd.Parameters.AddWithValue("@key", entry.Key);
            cmd.Parameters.AddWithValue("@value", (object?)entry.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@section", entry.Section);
            cmd.Parameters.AddWithValue("@updatedAt", entry.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@updatedBy", entry.UpdatedBy);
            cmd.Parameters.AddWithValue("@isSensitive", entry.IsSensitive ? 1 : 0);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
