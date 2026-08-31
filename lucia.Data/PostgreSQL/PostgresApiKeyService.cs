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
    private const long ApiKeyMutationLockKey = 0x4C5543494B455953;
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresApiKeyService> _logger;

    public PostgresApiKeyService(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory,
        ILogger<PostgresApiKeyService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<ApiKeyCreateResponse> CreateKeyAsync(
        string name,
        CancellationToken cancellationToken = default,
        bool isAdministrator = false)
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
        cmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(ApiKeyScopes.Create(isAdministrator)),
        });

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

    public async Task<ApiKeyCreateResponse?> CreateAdministratorKeyIfNoneAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory
            .CreateConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await AcquireMutationLockAsync(
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        await using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = """
            SELECT COUNT(*)
            FROM api_keys
            WHERE is_revoked = FALSE
              AND (expires_at IS NULL OR expires_at > @now)
              AND (name = @name OR scopes ? @scope);
            """;
        countCommand.Parameters.AddWithValue("now", DateTime.UtcNow);
        countCommand.Parameters.AddWithValue(
            "scope",
            AuthOptions.AdministratorScope);
        countCommand.Parameters.AddWithValue("name", name);
        var administratorCount = Convert.ToInt64(await countCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false));
        if (administratorCount > 0)
        {
            return null;
        }

        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var prefix =
            plaintextKey[..Math.Min(12, plaintextKey.Length)] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;
        await using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO api_keys
                (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES
                (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        insertCommand.Parameters.AddWithValue("id", id);
        insertCommand.Parameters.AddWithValue("keyHash", hash);
        insertCommand.Parameters.AddWithValue("keyPrefix", prefix);
        insertCommand.Parameters.AddWithValue("name", name);
        insertCommand.Parameters.AddWithValue("createdAt", createdAt);
        insertCommand.Parameters.Add(new NpgsqlParameter(
            "scopes",
            NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(
                ApiKeyScopes.Create(isAdministrator: true)),
        });
        await insertCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken)
            .ConfigureAwait(false);
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
        cmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(ApiKeyScopes.Create(isAdministrator: false)),
        });

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
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await AcquireMutationLockAsync(
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        await using var entryCommand = connection.CreateCommand();
        entryCommand.Transaction = transaction;
        entryCommand.CommandText = """
            SELECT name, key_prefix, is_revoked, expires_at, scopes::text
            FROM api_keys
            WHERE id = @id
            FOR UPDATE;
            """;
        entryCommand.Parameters.AddWithValue("id", keyId);
        await using var entryReader = await entryCommand
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await entryReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        var name = entryReader.GetString(0);
        var prefix = entryReader.GetString(1);
        var isRevoked = entryReader.GetBoolean(2);
        DateTime? expiresAt = entryReader.IsDBNull(3)
            ? null
            : entryReader.GetFieldValue<DateTime>(3);
        var scopes = JsonSerializer.Deserialize<string[]>(
            entryReader.GetString(4)) ?? ["*"];
        await entryReader.DisposeAsync().ConfigureAwait(false);
        if (isRevoked)
        {
            return false;
        }

        await using var activeCountCommand = connection.CreateCommand();
        activeCountCommand.Transaction = transaction;
        activeCountCommand.CommandText = """
            SELECT COUNT(*)
            FROM api_keys
            WHERE is_revoked = FALSE
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        activeCountCommand.Parameters.AddWithValue("now", revokedAt);
        var activeCount = Convert.ToInt64(await activeCountCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false));
        var isActive = !expiresAt.HasValue || expiresAt > revokedAt;
        if (isActive && activeCount <= 1)
        {
            throw new InvalidOperationException("Cannot revoke the last active API key. Create a new key first.");
        }
        if (isActive && scopes.Contains(
                AuthOptions.AdministratorScope,
                StringComparer.Ordinal))
        {
            await using var administratorCountCommand =
                connection.CreateCommand();
            administratorCountCommand.Transaction = transaction;
            administratorCountCommand.CommandText = """
                SELECT COUNT(*)
                FROM api_keys
                WHERE is_revoked = FALSE
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND scopes ? @scope;
                """;
            administratorCountCommand.Parameters.AddWithValue(
                "now",
                revokedAt);
            administratorCountCommand.Parameters.AddWithValue(
                "scope",
                AuthOptions.AdministratorScope);
            var administratorCount = Convert.ToInt64(
                await administratorCountCommand
                    .ExecuteScalarAsync(cancellationToken)
                    .ConfigureAwait(false));
            if (administratorCount <= 1)
            {
                throw new InvalidOperationException(
                    "Cannot revoke the last active administrator key. Regenerate it instead.");
            }
        }

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE api_keys
            SET is_revoked = TRUE, revoked_at = @revokedAt
            WHERE id = @id
              AND is_revoked = FALSE
            RETURNING name, key_prefix;
            """;
        cmd.Parameters.AddWithValue("id", keyId);
        cmd.Parameters.AddWithValue("revokedAt", revokedAt);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            await reader.DisposeAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            LogRevokedKey(_logger, name, prefix);
            return true;
        }

        return false;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory
            .CreateConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await AcquireMutationLockAsync(
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        await using var getCmd = connection.CreateCommand();
        getCmd.Transaction = transaction;
        getCmd.CommandText = """
            SELECT name, scopes::text
            FROM api_keys
            WHERE id = @id
            FOR UPDATE;
            """;
        getCmd.Parameters.AddWithValue("id", keyId);
        await using var reader = await getCmd
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"API key with ID '{keyId}' not found.");
        }
        var name = reader.GetString(0);
        var scopes =
            JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? ["*"];
        await reader.DisposeAsync().ConfigureAwait(false);

        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var prefix =
            plaintextKey[..Math.Min(12, plaintextKey.Length)] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;
        await using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO api_keys
                (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES
                (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        insertCommand.Parameters.AddWithValue("id", id);
        insertCommand.Parameters.AddWithValue("keyHash", hash);
        insertCommand.Parameters.AddWithValue("keyPrefix", prefix);
        insertCommand.Parameters.AddWithValue("name", name);
        insertCommand.Parameters.AddWithValue("createdAt", createdAt);
        insertCommand.Parameters.Add(new NpgsqlParameter(
            "scopes",
            NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(scopes),
        });
        await insertCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var revokeCommand = connection.CreateCommand();
        revokeCommand.Transaction = transaction;
        revokeCommand.CommandText = """
            UPDATE api_keys
            SET is_revoked = TRUE, revoked_at = @revokedAt
            WHERE id = @id;
            """;
        revokeCommand.Parameters.AddWithValue("id", keyId);
        revokeCommand.Parameters.AddWithValue("revokedAt", createdAt);
        await revokeCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken)
            .ConfigureAwait(false);
        LogRegeneratedKey(_logger, name);
        return new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = id,
            Prefix = prefix,
            Name = name,
            CreatedAt = createdAt,
        };
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
        string name,
        string plaintextKey,
        CancellationToken cancellationToken = default,
        bool isAdministrator = false)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
            return (null, 0);

        var hash = HashKey(plaintextKey);
        var scopes = JsonSerializer.Serialize(
            ApiKeyScopes.Create(isAdministrator));
        await using var connection = await _connectionFactory
            .CreateConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await AcquireMutationLockAsync(
                connection,
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        await using var checkCmd = connection.CreateCommand();
        checkCmd.Transaction = transaction;
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
        {
            if (isAdministrator)
            {
                await using var scopeCommand = connection.CreateCommand();
                scopeCommand.Transaction = transaction;
                scopeCommand.CommandText = """
                    UPDATE api_keys
                    SET scopes = @scopes
                    WHERE name = @name AND key_hash = @hash;
                    """;
                scopeCommand.Parameters.Add(new NpgsqlParameter(
                    "scopes",
                    NpgsqlDbType.Jsonb)
                {
                    Value = scopes,
                });
                scopeCommand.Parameters.AddWithValue("name", name);
                scopeCommand.Parameters.AddWithValue("hash", hash);
                await scopeCommand.ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            return (null, 0);
        }

        var prefix = plaintextKey.Length <= 12 ? plaintextKey : plaintextKey[..12] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;
        await using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = """
            INSERT INTO api_keys (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes)
            ON CONFLICT (key_hash) DO UPDATE SET
                key_prefix = EXCLUDED.key_prefix,
                name = EXCLUDED.name,
                created_at = EXCLUDED.created_at,
                last_used_at = NULL,
                expires_at = NULL,
                is_revoked = FALSE,
                revoked_at = NULL,
                scopes = EXCLUDED.scopes
            RETURNING id;
            """;
        insertCmd.Parameters.AddWithValue("id", id);
        insertCmd.Parameters.AddWithValue("keyHash", hash);
        insertCmd.Parameters.AddWithValue("keyPrefix", prefix);
        insertCmd.Parameters.AddWithValue("name", name);
        insertCmd.Parameters.AddWithValue("createdAt", createdAt);
        insertCmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Jsonb)
        {
            Value = scopes,
        });
        var replacementId = (string)(await insertCmd
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false))!;
        await using var revokeCmd = connection.CreateCommand();
        revokeCmd.Transaction = transaction;
        revokeCmd.CommandText = """
            UPDATE api_keys
            SET is_revoked = TRUE, revoked_at = @revokedAt
            WHERE name = @name
              AND key_hash <> @hash
              AND is_revoked = FALSE;
            """;
        revokeCmd.Parameters.AddWithValue("revokedAt", createdAt);
        revokeCmd.Parameters.AddWithValue("name", name);
        revokeCmd.Parameters.AddWithValue("hash", hash);
        var revokedCount = await revokeCmd
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken)
            .ConfigureAwait(false);

        LogOverrideEnvKey(_logger, name, prefix);

        return (new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = replacementId,
            Prefix = prefix,
            Name = name,
            CreatedAt = createdAt,
        }, revokedCount);
    }

    private static async Task AcquireMutationLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(@lockKey);";
        command.Parameters.AddWithValue("lockKey", ApiKeyMutationLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
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
