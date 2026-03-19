using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using Microsoft.Data.Sqlite;
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

    public SqliteApiKeyService(SqliteConnectionFactory connectionFactory, ILogger<SqliteApiKeyService> logger)
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
        cmd.Parameters.AddWithValue("@scopes", JsonSerializer.Serialize(new[] { "*" }));

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
        cmd.Parameters.AddWithValue("@scopes", JsonSerializer.Serialize(new[] { "*" }));

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

        // Fire-and-forget last-used update (intentionally uses CancellationToken.None
        // so the update completes even if the request ends)
        _ = Task.Run(async () =>
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                using var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = "UPDATE api_keys SET last_used_at = @lastUsedAt WHERE id = @id;";
                updateCmd.Parameters.AddWithValue("@lastUsedAt", DateTime.UtcNow.ToString("O"));
                updateCmd.Parameters.AddWithValue("@id", entry.Id);
                await updateCmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update LastUsedAt for API key '{KeyId}'", entry.Id);
            }
        });

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

    public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        // Lockout prevention: don't revoke the last active key
        var activeCount = await GetActiveKeyCountAsync(cancellationToken).ConfigureAwait(false);

        using var connection = _connectionFactory.CreateConnection();

        // Check if key exists and is not already revoked
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT is_revoked FROM api_keys WHERE id = @id;";
            checkCmd.Parameters.AddWithValue("@id", keyId);
            var result = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (result is null)
                return false;

            if ((long)result != 0)
                return false;
        }

        if (activeCount <= 1)
            throw new InvalidOperationException("Cannot revoke the last active API key. Create a new key first.");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE api_keys SET is_revoked = 1, revoked_at = @revokedAt
            WHERE id = @id AND is_revoked = 0;
            """;
        cmd.Parameters.AddWithValue("@id", keyId);
        cmd.Parameters.AddWithValue("@revokedAt", DateTime.UtcNow.ToString("O"));

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected > 0)
        {
            using var nameCmd = connection.CreateCommand();
            nameCmd.CommandText = "SELECT name, key_prefix FROM api_keys WHERE id = @id;";
            nameCmd.Parameters.AddWithValue("@id", keyId);
            using var reader = await nameCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Revoked API key '{Name}' ({Prefix})", reader.GetString(0), reader.GetString(1));
            }
        }

        return affected > 0;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        string name;

        using (var connection = _connectionFactory.CreateConnection())
        {
            using var getCmd = connection.CreateCommand();
            getCmd.CommandText = "SELECT name FROM api_keys WHERE id = @id;";
            getCmd.Parameters.AddWithValue("@id", keyId);
            var result = await getCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (result is not string keyName)
                throw new InvalidOperationException($"API key with ID '{keyId}' not found.");

            name = keyName;

            // Revoke old key (bypass lockout check since we're creating a replacement)
            using var revokeCmd = connection.CreateCommand();
            revokeCmd.CommandText = "UPDATE api_keys SET is_revoked = 1, revoked_at = @revokedAt WHERE id = @id;";
            revokeCmd.Parameters.AddWithValue("@id", keyId);
            revokeCmd.Parameters.AddWithValue("@revokedAt", DateTime.UtcNow.ToString("O"));
            await revokeCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var newKey = await CreateKeyAsync(name, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Regenerated API key '{Name}' \u2014 old key revoked, new key created", name);

        return newKey;
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
