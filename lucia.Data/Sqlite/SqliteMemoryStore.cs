using System.Globalization;
using System.Linq;

using lucia.Agents.Abstractions;
using lucia.Agents.Models;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

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
    public SqliteMemoryStore(
        [FromKeyedServices(SqliteDbNames.Config)] SqliteConnectionFactory connectionFactory)
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
        await StoreCoreAsync(userId, key, value, createdAt, expiresAt, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task StoreAsync(string userId, string key, string value, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var createdAt = DateTime.UtcNow;
        var normalizedExpiresAt = expiresAt.UtcDateTime;
        return StoreCoreAsync(userId, key, value, createdAt, normalizedExpiresAt, ct);
    }

    private async Task StoreCoreAsync(string userId, string key, string value, DateTime createdAt, DateTime? expiresAt, CancellationToken ct)
    {
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
    public Task<IReadOnlyList<MemoryEntry>> SearchAsync(string userId, string? query = null, int limit = 20, CancellationToken ct = default) =>
        SearchCoreAsync(userId, query, limit, false, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MemoryEntry>> SearchPersonalAsync(string userId, string? query = null, int limit = 20, CancellationToken ct = default) =>
        SearchCoreAsync(userId, query, limit, true, ct);

    private async Task<IReadOnlyList<MemoryEntry>> SearchCoreAsync(string userId, string? query, int limit, bool personalOnly, CancellationToken ct)
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
                  AND (@personalOnly = 0 OR lower(substr(ltrim(key, @whitespace), 1, length(@historyPrefix))) <> @historyPrefix)
                ORDER BY created_at DESC
                LIMIT @limit;
                """;
        }
        else
        {
            var escapedQuery = SqlLikePattern.Escape(query);
            cmd.CommandText = """
                SELECT key, value, created_at, expires_at
                FROM user_memories
                WHERE user_id = @userId
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND (@personalOnly = 0 OR lower(substr(ltrim(key, @whitespace), 1, length(@historyPrefix))) <> @historyPrefix)
                  AND (key LIKE @query ESCAPE '\' OR value LIKE @query ESCAPE '\')
                ORDER BY created_at DESC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@query", $"%{escapedQuery}%");
        }

        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@personalOnly", personalOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("@whitespace", MemoryKeys.LeadingWhitespace);
        cmd.Parameters.AddWithValue("@historyPrefix", MemoryKeys.ChatHistoryPrefix);

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
