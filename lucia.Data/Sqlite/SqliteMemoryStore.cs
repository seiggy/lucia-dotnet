using System.Globalization;
using System.Linq;

using lucia.Agents.Abstractions;
using lucia.Agents.Models;

using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IMemoryStore"/>.
/// </summary>
public sealed class SqliteMemoryStore : IMemoryStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteMemoryStore"/> class.
    /// </summary>
    public SqliteMemoryStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc/>
    public async Task StoreAsync(string userId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var createdAt = DateTime.UtcNow;
        var expiresAt = ttl.HasValue ? createdAt.Add(ttl.Value) : (DateTime?)null;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_memories (user_id, key, value, created_at, expires_at)
            VALUES (@userId, @key, @value, @createdAt, @expiresAt)
            ON CONFLICT(user_id, key) DO UPDATE SET
                value = excluded.value,
                created_at = excluded.created_at,
                expires_at = excluded.expires_at;
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@expiresAt", expiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> RetrieveAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var connection = _connectionFactory.CreateConnection();
        await DeleteExpiredEntriesAsync(connection, userId, ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT value
            FROM user_memories
            WHERE user_id = @userId AND key = @key AND (expires_at IS NULL OR expires_at > @now);
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string value ? value : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(string userId, string? query = null, int limit = 20, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (limit <= 0)
        {
            return [];
        }

        using var connection = _connectionFactory.CreateConnection();
        await DeleteExpiredEntriesAsync(connection, userId, ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(query))
        {
            cmd.CommandText = """
                SELECT key, value, created_at, expires_at
                FROM user_memories
                WHERE user_id = @userId AND (expires_at IS NULL OR expires_at > @now)
                ORDER BY created_at DESC
                LIMIT @limit;
                """;
        }
        else
        {
            cmd.CommandText = """
                SELECT key, value, created_at, expires_at
                FROM user_memories
                WHERE user_id = @userId
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND (key LIKE @query OR value LIKE @query)
                ORDER BY created_at DESC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@query", $"%{query}%");
        }

        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@limit", limit);

        return await ReadEntriesAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM user_memories WHERE user_id = @userId AND key = @key;";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@key", key);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        using var connection = _connectionFactory.CreateConnection();
        await DeleteExpiredEntriesAsync(connection, userId, ct).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT key, value, created_at, expires_at
            FROM user_memories
            WHERE user_id = @userId AND (expires_at IS NULL OR expires_at > @now)
            ORDER BY created_at DESC
            LIMIT 200;
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        return await ReadEntriesAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task DeleteExpiredEntriesAsync(SqliteConnection connection, string userId, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM user_memories WHERE user_id = @userId AND expires_at IS NOT NULL AND expires_at <= @now;";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<MemoryEntry>> ReadEntriesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var entries = new List<MemoryEntry>();

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            entries.Add(new MemoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(3)
                    ? null
                    : DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return entries;
    }
}
