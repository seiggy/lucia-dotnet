using lucia.Agents.Abstractions;
using lucia.Agents.Models;

using Npgsql;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IMemoryStore"/>.
/// </summary>
public sealed class PostgresMemoryStore : IMemoryStore
{
    private readonly PostgresConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresMemoryStore"/> class.
    /// </summary>
    public PostgresMemoryStore(PostgresConnectionFactory connectionFactory)
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

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_memories (user_id, key, value, created_at, expires_at)
            VALUES (@userId, @key, @value, @createdAt, @expiresAt)
            ON CONFLICT (user_id, key) DO UPDATE SET
                value = EXCLUDED.value,
                created_at = EXCLUDED.created_at,
                expires_at = EXCLUDED.expires_at;
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.Parameters.AddWithValue("createdAt", createdAt);
        cmd.Parameters.AddWithValue("expiresAt", (object?)expiresAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> RetrieveAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await DeleteExpiredEntriesAsync(connection, userId, ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT value
            FROM user_memories
            WHERE user_id = @userId
              AND key = @key
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);

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

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await DeleteExpiredEntriesAsync(connection, userId, ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(query))
        {
            cmd.CommandText = """
                SELECT key, value, created_at, expires_at
                FROM user_memories
                WHERE user_id = @userId
                  AND (expires_at IS NULL OR expires_at > @now)
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
                  AND (key ILIKE @query OR value ILIKE @query)
                ORDER BY created_at DESC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("query", $"%{query}%");
        }

        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("limit", limit);

        return await ReadEntriesAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM user_memories WHERE user_id = @userId AND key = @key;";
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("key", key);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await DeleteExpiredEntriesAsync(connection, userId, ct).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT key, value, created_at, expires_at
            FROM user_memories
            WHERE user_id = @userId
              AND (expires_at IS NULL OR expires_at > @now)
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);

        return await ReadEntriesAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task DeleteExpiredEntriesAsync(NpgsqlConnection connection, string userId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM user_memories
            WHERE user_id = @userId
              AND expires_at IS NOT NULL
              AND expires_at <= @now;
            """;
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<MemoryEntry>> ReadEntriesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var entries = new List<MemoryEntry>();

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            entries.Add(new MemoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTime>(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTime>(3)));
        }

        return entries;
    }
}
