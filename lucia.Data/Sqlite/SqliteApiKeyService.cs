using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed API key service. Stores SHA-256 hashes of keys, never plaintext.
/// Crypto logic is identical to <c>MongoApiKeyService</c>.
/// </summary>
public sealed class SqliteApiKeyService : IApiKeyService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteApiKeyService> _logger;

    public SqliteApiKeyService(
        [FromKeyedServices(SqliteDbNames.Config)] SqliteConnectionFactory connectionFactory,
        ILogger<SqliteApiKeyService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public Task<ApiKeyCreateResponse> CreateKeyAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        CreateKeyCoreAsync(name, isAdministrator: false, cancellationToken);

    public Task<ApiKeyCreateResponse> CreateAdministratorKeyAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        CreateKeyCoreAsync(name, isAdministrator: true, cancellationToken);

    private async Task<ApiKeyCreateResponse> CreateKeyCoreAsync(
        string name,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey[..Math.Min(12, plaintextKey.Length)] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO api_keys (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@keyHash", hash);
        cmd.Parameters.AddWithValue("@keyPrefix", prefix);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));
        cmd.Parameters.AddWithValue(
            "@scopes",
            JsonSerializer.Serialize(ApiKeyScopes.Create(isAdministrator)));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created API key '{Name}' with prefix {Prefix}", name, prefix);

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
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = """
            SELECT COUNT(*)
            FROM api_keys
            WHERE is_revoked = 0
              AND (expires_at IS NULL OR expires_at > @now)
              AND EXISTS (
                  SELECT 1
                  FROM json_each(api_keys.scopes)
                  WHERE value = @scope
              );
            """;
        countCommand.Parameters.AddWithValue(
            "@now",
            DateTime.UtcNow.ToString("O"));
        countCommand.Parameters.AddWithValue(
            "@scope",
            AuthOptions.AdministratorScope);
        var administratorCount = (long)(await countCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false))!;
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
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO api_keys
                (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES
                (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        insertCommand.Parameters.AddWithValue("@id", id);
        insertCommand.Parameters.AddWithValue("@keyHash", hash);
        insertCommand.Parameters.AddWithValue("@keyPrefix", prefix);
        insertCommand.Parameters.AddWithValue("@name", name);
        insertCommand.Parameters.AddWithValue(
            "@createdAt",
            createdAt.ToString("O"));
        insertCommand.Parameters.AddWithValue(
            "@scopes",
            JsonSerializer.Serialize(
                ApiKeyScopes.Create(isAdministrator: true)));
        await insertCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        transaction.Commit();
        return new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = id,
            Prefix = prefix,
            Name = name,
            CreatedAt = createdAt,
        };
    }

    public async Task<ApiKeyCreateResponse?> CreateKeyFromPlaintextAsync(
        string name,
        string plaintextKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
            return null;

        var existingKeys = await ListKeysAsync(cancellationToken).ConfigureAwait(false);
        if (existingKeys.Any(k => k.Name == name && !k.IsRevoked))
            return null;

        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey.Length <= 12 ? plaintextKey : plaintextKey[..12] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO api_keys (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@keyHash", hash);
        cmd.Parameters.AddWithValue("@keyPrefix", prefix);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));
        cmd.Parameters.AddWithValue(
            "@scopes",
            JsonSerializer.Serialize(ApiKeyScopes.Create(isAdministrator: false)));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created API key '{Name}' from env (prefix {Prefix})", name, prefix);

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
            return null;

        var hash = HashKey(plaintextKey);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, key_hash, key_prefix, name, created_at, last_used_at,
                   expires_at, is_revoked, revoked_at, scopes
            FROM api_keys
            WHERE key_hash = @hash AND is_revoked = 0;
            """;
        cmd.Parameters.AddWithValue("@hash", hash);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var entry = ReadApiKeyEntry(reader);

        // Check expiration
        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
            return null;

        return entry;
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, key_hash, key_prefix, name, created_at, last_used_at,
                   expires_at, is_revoked, revoked_at, scopes
            FROM api_keys
            ORDER BY created_at DESC;
            """;

        var summaries = new List<ApiKeySummary>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            summaries.Add(new ApiKeySummary
            {
                Id = reader.GetString(0),
                KeyPrefix = reader.GetString(2),
                Name = reader.GetString(3),
                CreatedAt = DateTime.Parse(reader.GetString(4)),
                LastUsedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                ExpiresAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                IsRevoked = reader.GetInt64(7) != 0,
                RevokedAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                Scopes = JsonSerializer.Deserialize<string[]>(reader.GetString(9)) ?? ["*"],
            });
        }

        return summaries;
    }

    public async Task<ApiKeySummary?> GetKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, key_hash, key_prefix, name, created_at, last_used_at,
                   expires_at, is_revoked, revoked_at, scopes
            FROM api_keys
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", keyId);
        using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        return new ApiKeySummary
        {
            Id = reader.GetString(0),
            KeyPrefix = reader.GetString(2),
            Name = reader.GetString(3),
            CreatedAt = DateTime.Parse(reader.GetString(4)),
            LastUsedAt = reader.IsDBNull(5)
                ? null
                : DateTime.Parse(reader.GetString(5)),
            ExpiresAt = reader.IsDBNull(6)
                ? null
                : DateTime.Parse(reader.GetString(6)),
            IsRevoked = reader.GetInt64(7) != 0,
            RevokedAt = reader.IsDBNull(8)
                ? null
                : DateTime.Parse(reader.GetString(8)),
            Scopes =
                JsonSerializer.Deserialize<string[]>(reader.GetString(9))
                ?? ["*"],
        };
    }

    public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var entryCommand = connection.CreateCommand();
        entryCommand.Transaction = transaction;
        entryCommand.CommandText = """
            SELECT name, key_prefix, is_revoked, expires_at, scopes
            FROM api_keys
            WHERE id = @id;
            """;
        entryCommand.Parameters.AddWithValue("@id", keyId);
        using var reader = await entryCommand
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }
        var name = reader.GetString(0);
        var prefix = reader.GetString(1);
        var isRevoked = reader.GetInt64(2) != 0;
        DateTime? expiresAt = reader.IsDBNull(3)
            ? null
            : DateTime.Parse(reader.GetString(3));
        var scopes =
            JsonSerializer.Deserialize<string[]>(reader.GetString(4)) ?? ["*"];
        await reader.DisposeAsync().ConfigureAwait(false);
        if (isRevoked)
        {
            return false;
        }

        using var activeCountCommand = connection.CreateCommand();
        activeCountCommand.Transaction = transaction;
        activeCountCommand.CommandText = """
            SELECT COUNT(*)
            FROM api_keys
            WHERE is_revoked = 0
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        activeCountCommand.Parameters.AddWithValue("@now", now.ToString("O"));
        var activeCount = (long)(await activeCountCommand
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false))!;
        var isActive = !expiresAt.HasValue || expiresAt > now;
        if (isActive && activeCount <= 1)
            throw new InvalidOperationException("Cannot revoke the last active API key. Create a new key first.");
        if (isActive && scopes.Contains(
                AuthOptions.AdministratorScope,
                StringComparer.Ordinal))
        {
            using var administratorCountCommand = connection.CreateCommand();
            administratorCountCommand.Transaction = transaction;
            administratorCountCommand.CommandText = """
                SELECT COUNT(*)
                FROM api_keys
                WHERE is_revoked = 0
                  AND (expires_at IS NULL OR expires_at > @now)
                  AND EXISTS (
                      SELECT 1
                      FROM json_each(api_keys.scopes)
                      WHERE value = @scope
                  );
                """;
            administratorCountCommand.Parameters.AddWithValue(
                "@now",
                now.ToString("O"));
            administratorCountCommand.Parameters.AddWithValue(
                "@scope",
                AuthOptions.AdministratorScope);
            var administratorCount = (long)(await administratorCountCommand
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false))!;
            if (administratorCount <= 1)
            {
                throw new InvalidOperationException(
                    "Cannot revoke the last active administrator key. Regenerate it instead.");
            }
        }

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE api_keys SET is_revoked = 1, revoked_at = @revokedAt
            WHERE id = @id AND is_revoked = 0;
            """;
        cmd.Parameters.AddWithValue("@id", keyId);
        cmd.Parameters.AddWithValue("@revokedAt", DateTime.UtcNow.ToString("O"));

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
        if (affected > 0)
        {
            _logger.LogInformation(
                "Revoked API key '{Name}' ({Prefix})",
                name,
                prefix);
        }

        return affected > 0;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var getCmd = connection.CreateCommand();
        getCmd.Transaction = transaction;
        getCmd.CommandText = "SELECT name, scopes FROM api_keys WHERE id = @id;";
        getCmd.Parameters.AddWithValue("@id", keyId);
        using var reader = await getCmd
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
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO api_keys
                (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES
                (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes);
            """;
        insertCommand.Parameters.AddWithValue("@id", id);
        insertCommand.Parameters.AddWithValue("@keyHash", hash);
        insertCommand.Parameters.AddWithValue("@keyPrefix", prefix);
        insertCommand.Parameters.AddWithValue("@name", name);
        insertCommand.Parameters.AddWithValue(
            "@createdAt",
            createdAt.ToString("O"));
        insertCommand.Parameters.AddWithValue(
            "@scopes",
            JsonSerializer.Serialize(scopes));
        await insertCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        using var revokeCommand = connection.CreateCommand();
        revokeCommand.Transaction = transaction;
        revokeCommand.CommandText = """
            UPDATE api_keys
            SET is_revoked = 1, revoked_at = @revokedAt
            WHERE id = @id;
            """;
        revokeCommand.Parameters.AddWithValue("@id", keyId);
        revokeCommand.Parameters.AddWithValue(
            "@revokedAt",
            createdAt.ToString("O"));
        await revokeCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        transaction.Commit();
        _logger.LogInformation("Regenerated API key '{Name}' \u2014 old key revoked, new key created", name);

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
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM api_keys
            WHERE is_revoked = 0
            AND (expires_at IS NULL OR expires_at > @now);
            """;
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count ? (int)count : 0;
    }

    public async Task<bool> HasAnyKeysAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM api_keys;";

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long count && count > 0;
    }

    public Task<(ApiKeyCreateResponse? Created, int RevokedCount)>
        OverrideKeyFromPlaintextAsync(
            string name,
            string plaintextKey,
            CancellationToken cancellationToken = default) =>
            OverrideKeyFromPlaintextCoreAsync(
                name,
                plaintextKey,
                isAdministrator: false,
                cancellationToken);

    public Task<(ApiKeyCreateResponse? Created, int RevokedCount)>
        OverrideAdministratorKeyFromPlaintextAsync(
            string name,
            string plaintextKey,
            CancellationToken cancellationToken = default) =>
            OverrideKeyFromPlaintextCoreAsync(
                name,
                plaintextKey,
                isAdministrator: true,
                cancellationToken);

    private async Task<(ApiKeyCreateResponse? Created, int RevokedCount)>
        OverrideKeyFromPlaintextCoreAsync(
        string name,
        string plaintextKey,
        bool isAdministrator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
            return (null, 0);

        var hash = HashKey(plaintextKey);
        var scopes = JsonSerializer.Serialize(
            ApiKeyScopes.Create(isAdministrator));
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var checkCmd = connection.CreateCommand();
        checkCmd.Transaction = transaction;
        checkCmd.CommandText = """
            SELECT COUNT(*) FROM api_keys
            WHERE name = @name AND key_hash = @hash AND is_revoked = 0
              AND (expires_at IS NULL OR expires_at > @now);
            """;
        checkCmd.Parameters.AddWithValue("@name", name);
        checkCmd.Parameters.AddWithValue("@hash", hash);
        checkCmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        var matchCount = (long)(await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        if (matchCount > 0)
        {
            if (isAdministrator)
            {
                using var scopeCommand = connection.CreateCommand();
                scopeCommand.Transaction = transaction;
                scopeCommand.CommandText = """
                    UPDATE api_keys
                    SET scopes = @scopes
                    WHERE name = @name AND key_hash = @hash;
                    """;
                scopeCommand.Parameters.AddWithValue("@scopes", scopes);
                scopeCommand.Parameters.AddWithValue("@name", name);
                scopeCommand.Parameters.AddWithValue("@hash", hash);
                await scopeCommand.ExecuteNonQueryAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            transaction.Commit();
            return (null, 0);
        }

        var prefix = plaintextKey.Length <= 12 ? plaintextKey : plaintextKey[..12] + "...";
        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;
        using var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = transaction;
        insertCmd.CommandText = """
            INSERT INTO api_keys
                (id, key_hash, key_prefix, name, created_at, scopes)
            VALUES
                (@id, @keyHash, @keyPrefix, @name, @createdAt, @scopes)
            ON CONFLICT(key_hash) DO UPDATE SET
                key_prefix = excluded.key_prefix,
                name = excluded.name,
                created_at = excluded.created_at,
                last_used_at = NULL,
                expires_at = NULL,
                is_revoked = 0,
                revoked_at = NULL,
                scopes = excluded.scopes
            RETURNING id;
            """;
        insertCmd.Parameters.AddWithValue("@id", id);
        insertCmd.Parameters.AddWithValue("@keyHash", hash);
        insertCmd.Parameters.AddWithValue("@keyPrefix", prefix);
        insertCmd.Parameters.AddWithValue("@name", name);
        insertCmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O"));
        insertCmd.Parameters.AddWithValue("@scopes", scopes);
        var replacementId = (string)(await insertCmd
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false))!;
        using var revokeCmd = connection.CreateCommand();
        revokeCmd.Transaction = transaction;
        revokeCmd.CommandText = """
            UPDATE api_keys
            SET is_revoked = 1, revoked_at = @revokedAt
            WHERE name = @name
              AND key_hash <> @hash
              AND is_revoked = 0;
            """;
        revokeCmd.Parameters.AddWithValue(
            "@revokedAt",
            createdAt.ToString("O"));
        revokeCmd.Parameters.AddWithValue("@name", name);
        revokeCmd.Parameters.AddWithValue("@hash", hash);
        var revokedCount = await revokeCmd
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        transaction.Commit();

        _logger.LogInformation("Override API key '{Name}' from env (prefix {Prefix})", name, prefix);

        return (new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = replacementId,
            Prefix = prefix,
            Name = name,
            CreatedAt = createdAt,
        }, revokedCount);
    }

    // ── Crypto helpers (identical to MongoApiKeyService) ────────

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

    private static ApiKeyEntry ReadApiKeyEntry(SqliteDataReader reader)
    {
        return new ApiKeyEntry
        {
            Id = reader.GetString(0),
            KeyHash = reader.GetString(1),
            KeyPrefix = reader.GetString(2),
            Name = reader.GetString(3),
            CreatedAt = DateTime.Parse(reader.GetString(4)),
            LastUsedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
            ExpiresAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
            IsRevoked = reader.GetInt64(7) != 0,
            RevokedAt = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
            Scopes = JsonSerializer.Deserialize<string[]>(reader.GetString(9)) ?? ["*"],
        };
    }
}
