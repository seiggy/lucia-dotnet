using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using lucia.Agents.Abstractions;
using lucia.Agents.Auth;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed API key service. Stores SHA-256 hashes of keys, never plaintext.
/// </summary>
public sealed partial class PostgresApiKeyService : IApiKeyService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresApiKeyService> _logger;

    public PostgresApiKeyService(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory,
        ILogger<PostgresApiKeyService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<ApiKeyCreateResponse> CreateKeyAsync(string name, CancellationToken cancellationToken = default)
    {
        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey[..Math.Min(12, plaintextKey.Length)] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO api_keys (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("keyHash", hash);
        cmd.Parameters.AddWithValue("keyPrefix", prefix);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("createdAt", createdAt);
        cmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(new[] { "*" }) });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogCreatedKey(_logger, name, prefix);

        return new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = id,
            Prefix = prefix,
            Name = name,
            CreatedAt = createdAt,
        };
    }

    public async Task<ApiKeyCreateResponse?> CreateKeyFromPlaintextAsync(string name, string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
        {
            return null;
        }

        var existingKeys = await ListKeysAsync(cancellationToken).ConfigureAwait(false);
        if (existingKeys.Any(key => !key.IsRevoked && key.Name == name))
        {
            return null;
        }

        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey.Length <= 12 ? plaintextKey : plaintextKey[..12] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO api_keys (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("keyHash", hash);
        cmd.Parameters.AddWithValue("keyPrefix", prefix);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("createdAt", createdAt);
        cmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(new[] { "*" }) });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogCreatedEnvKey(_logger, name, prefix);

        return new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = id,
            Prefix = prefix,
            Name = name,
            CreatedAt = createdAt,
        };
    }

    public async Task<ApiKeyEntry?> ValidateKeyAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return null;
        }

        var hash = HashKey(plaintextKey);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, key_hash, key_prefix, name, created_at, last_used_at,
                   expires_at, is_revoked, revoked_at, scopes::text
            FROM api_keys
            WHERE key_hash = @hash AND is_revoked = FALSE;
            """;
        cmd.Parameters.AddWithValue("hash", hash);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var entry = ReadApiKeyEntry(reader);
        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
        {
            return null;
        }

        return entry;
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, key_hash, key_prefix, name, created_at, last_used_at,
                   expires_at, is_revoked, revoked_at, scopes::text
            FROM api_keys
            ORDER BY created_at DESC;
            """;

        var summaries = new List<ApiKeySummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            summaries.Add(new ApiKeySummary
            {
                Id = reader.GetString(0),
                KeyPrefix = reader.GetString(2),
                Name = reader.GetString(3),
                CreatedAt = reader.GetFieldValue<DateTime>(4),
                LastUsedAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTime>(5),
                ExpiresAt = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTime>(6),
                IsRevoked = reader.GetBoolean(7),
                RevokedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTime>(8),
                Scopes = JsonSerializer.Deserialize<string[]>(reader.GetString(9)) ?? ["*"],
            });
        }

        return summaries;
    }

    public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var revokedAt = DateTime.UtcNow;

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE api_keys
            SET is_revoked = TRUE, revoked_at = @revokedAt
            WHERE id = @id
              AND is_revoked = FALSE
              AND (
                  SELECT COUNT(*)
                  FROM api_keys
                  WHERE is_revoked = FALSE
                    AND (expires_at IS NULL OR expires_at > @now)
              ) > 1
            RETURNING name, key_prefix;
            """;
        cmd.Parameters.AddWithValue("id", keyId);
        cmd.Parameters.AddWithValue("now", revokedAt);
        cmd.Parameters.AddWithValue("revokedAt", revokedAt);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            LogRevokedKey(_logger, reader.GetString(0), reader.GetString(1));
            return true;
        }

        await reader.DisposeAsync().ConfigureAwait(false);

        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT is_revoked FROM api_keys WHERE id = @id;";
        checkCmd.Parameters.AddWithValue("id", keyId);
        var result = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return false;
        }

        if (result is bool isRevoked && isRevoked)
        {
            return false;
        }

        var activeCount = await GetActiveKeyCountAsync(cancellationToken).ConfigureAwait(false);
        if (activeCount <= 1)
        {
            throw new InvalidOperationException("Cannot revoke the last active API key. Create a new key first.");
        }

        return false;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        string name;

        await using (var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using var getCmd = connection.CreateCommand();
            getCmd.CommandText = "SELECT name FROM api_keys WHERE id = @id;";
            getCmd.Parameters.AddWithValue("id", keyId);
            var result = await getCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (result is not string keyName)
            {
                throw new InvalidOperationException($"API key with ID '{keyId}' not found.");
            }

            name = keyName;

            await using var revokeCmd = connection.CreateCommand();
            revokeCmd.CommandText = "UPDATE api_keys SET is_revoked = TRUE, revoked_at = @revokedAt WHERE id = @id;";
            revokeCmd.Parameters.AddWithValue("id", keyId);
            revokeCmd.Parameters.AddWithValue("revokedAt", DateTime.UtcNow);
            await revokeCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var newKey = await CreateKeyAsync(name, cancellationToken).ConfigureAwait(false);
        LogRegeneratedKey(_logger, name);
        return newKey;
    }

    public async Task<int> GetActiveKeyCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM api_keys
            WHERE is_revoked = FALSE
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public async Task<bool> HasAnyKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM api_keys;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null and not DBNull && Convert.ToInt64(result) > 0;
    }

    public async Task<(ApiKeyCreateResponse? Created, int RevokedCount)> OverrideKeyFromPlaintextAsync(
        string name, string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
            return (null, 0);

        var hash = HashKey(plaintextKey);

        // Check if a non-revoked key with this name already hashes to the env value → no-op
        await using var checkConn = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var checkCmd = checkConn.CreateCommand();
        checkCmd.CommandText = """
            SELECT COUNT(*) FROM api_keys
            WHERE name = @name AND key_hash = @hash AND is_revoked = FALSE
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        checkCmd.Parameters.AddWithValue("name", name);
        checkCmd.Parameters.AddWithValue("hash", hash);
        checkCmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        var matchCount = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
        if (matchCount > 0)
            return (null, 0);

        // Revoke existing non-revoked keys with this name — bypass lockout because we are
        // creating a replacement immediately after.
        await using var revokeConn = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var revokeCmd = revokeConn.CreateCommand();
        revokeCmd.CommandText = """
            UPDATE api_keys SET is_revoked = TRUE, revoked_at = @revokedAt
            WHERE name = @name AND is_revoked = FALSE;
            """;
        revokeCmd.Parameters.AddWithValue("revokedAt", DateTime.UtcNow);
        revokeCmd.Parameters.AddWithValue("name", name);
        var revokedCount = await revokeCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Create the replacement key from the provided plaintext.
        var prefix = plaintextKey.Length <= 12 ? plaintextKey : plaintextKey[..12] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        await using var insertConn = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var insertCmd = insertConn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO api_keys (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes)
            ON CONFLICT (key_hash) DO NOTHING;
            """;
        insertCmd.Parameters.AddWithValue("id", id);
        insertCmd.Parameters.AddWithValue("keyHash", hash);
        insertCmd.Parameters.AddWithValue("keyPrefix", prefix);
        insertCmd.Parameters.AddWithValue("name", name);
        insertCmd.Parameters.AddWithValue("createdAt", createdAt);
        insertCmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(new[] { "*" }) });

        var inserted = await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 0)
        {
            // Concurrent seed: another process already inserted the same hash — idempotent.
            return (null, 0);
        }

        LogOverrideEnvKey(_logger, name, prefix);

        return (new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = id,
            Prefix = prefix,
            Name = name,
            CreatedAt = createdAt,
        }, revokedCount);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Created API key '{Name}' with prefix {Prefix}")]
    private static partial void LogCreatedKey(ILogger logger, string name, string prefix);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Created API key '{Name}' from env (prefix {Prefix})")]
    private static partial void LogCreatedEnvKey(ILogger logger, string name, string prefix);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Revoked API key '{Name}' ({Prefix})")]
    private static partial void LogRevokedKey(ILogger logger, string name, string prefix);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Regenerated API key '{Name}' \u2014 old key revoked, new key created")]
    private static partial void LogRegeneratedKey(ILogger logger, string name);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Override API key '{Name}' from env (prefix {Prefix})")]
    private static partial void LogOverrideEnvKey(ILogger logger, string name, string prefix);

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(AuthOptions.KeyLengthBytes);
        var encoded = Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
        return AuthOptions.KeyPrefix + encoded;
    }

    private static string HashKey(string plaintextKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey));
        return Convert.ToHexStringLower(bytes);
    }

    private static ApiKeyEntry ReadApiKeyEntry(NpgsqlDataReader reader)
    {
        return new ApiKeyEntry
        {
            Id = reader.GetString(0),
            KeyHash = reader.GetString(1),
            KeyPrefix = reader.GetString(2),
            Name = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTime>(4),
            LastUsedAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTime>(5),
            ExpiresAt = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTime>(6),
            IsRevoked = reader.GetBoolean(7),
            RevokedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTime>(8),
            Scopes = JsonSerializer.Deserialize<string[]>(reader.GetString(9)) ?? ["*"],
        };
    }
}
