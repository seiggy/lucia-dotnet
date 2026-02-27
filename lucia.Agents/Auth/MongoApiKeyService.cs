using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace lucia.Agents.Auth;

/// <summary>
/// MongoDB-backed API key service. Stores SHA-256 hashes of keys, never plaintext.
/// </summary>
public sealed class MongoApiKeyService : IApiKeyService
{
    private readonly IMongoCollection<ApiKeyEntry> _collection;
    private readonly ILogger<MongoApiKeyService> _logger;

    public MongoApiKeyService(IMongoClient mongoClient, ILogger<MongoApiKeyService> logger)
    {
        var database = mongoClient.GetDatabase(ApiKeyEntry.DatabaseName);
        _collection = database.GetCollection<ApiKeyEntry>(ApiKeyEntry.CollectionName);
        _logger = logger;

        EnsureIndexes();
    }

    public async Task<ApiKeyCreateResponse> CreateKeyAsync(string name, CancellationToken cancellationToken = default)
    {
        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey[..Math.Min(12, plaintextKey.Length)] + "...";

        var entry = new ApiKeyEntry
        {
            KeyHash = hash,
            KeyPrefix = prefix,
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };

        await _collection.InsertOneAsync(entry, cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created API key '{Name}' with prefix {Prefix}", name, prefix);

        return new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = entry.Id,
            Prefix = prefix,
            Name = name,
            CreatedAt = entry.CreatedAt,
        };
    }

    public async Task<ApiKeyCreateResponse?> CreateKeyFromPlaintextAsync(string name, string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
        {
            return null;
        }

        var existingKeys = await ListKeysAsync(cancellationToken).ConfigureAwait(false);
        if (existingKeys.Any(k => k.Name == name && !k.IsRevoked))
        {
            return null;
        }

        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey.Length <= 12 ? plaintextKey : plaintextKey[..12] + "...";

        var entry = new ApiKeyEntry
        {
            KeyHash = hash,
            KeyPrefix = prefix,
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };

        await _collection.InsertOneAsync(entry, cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created API key '{Name}' from env (prefix {Prefix})", name, prefix);

        return new ApiKeyCreateResponse
        {
            Key = plaintextKey,
            Id = entry.Id,
            Prefix = prefix,
            Name = name,
            CreatedAt = entry.CreatedAt,
        };
    }

    public async Task<ApiKeyEntry?> ValidateKeyAsync(string plaintextKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey))
        {
            return null;
        }

        var hash = HashKey(plaintextKey);

        var entry = await _collection
            .Find(k => k.KeyHash == hash && !k.IsRevoked)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return null;
        }

        // Check expiration
        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
        {
            return null;
        }

        // Update last used timestamp (fire-and-forget for performance)
        var update = Builders<ApiKeyEntry>.Update.Set(k => k.LastUsedAt, DateTime.UtcNow);
        _ = _collection.UpdateOneAsync(k => k.Id == entry.Id, update, cancellationToken: CancellationToken.None);

        return entry;
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _collection
            .Find(FilterDefinition<ApiKeyEntry>.Empty)
            .SortByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries.Select(e => new ApiKeySummary
        {
            Id = e.Id,
            KeyPrefix = e.KeyPrefix,
            Name = e.Name,
            CreatedAt = e.CreatedAt,
            LastUsedAt = e.LastUsedAt,
            ExpiresAt = e.ExpiresAt,
            IsRevoked = e.IsRevoked,
            RevokedAt = e.RevokedAt,
            Scopes = e.Scopes,
        }).ToList();
    }

    public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        // Lockout prevention: don't revoke the last active key
        var activeCount = await GetActiveKeyCountAsync(cancellationToken).ConfigureAwait(false);
        var entry = await _collection.Find(k => k.Id == keyId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (entry is null || entry.IsRevoked)
        {
            return false;
        }

        if (activeCount <= 1)
        {
            throw new InvalidOperationException("Cannot revoke the last active API key. Create a new key first.");
        }

        var update = Builders<ApiKeyEntry>.Update
            .Set(k => k.IsRevoked, true)
            .Set(k => k.RevokedAt, DateTime.UtcNow);

        var result = await _collection
            .UpdateOneAsync(k => k.Id == keyId && !k.IsRevoked, update, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ModifiedCount > 0)
        {
            _logger.LogInformation("Revoked API key '{Name}' ({Prefix})", entry.Name, entry.KeyPrefix);
        }

        return result.ModifiedCount > 0;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var entry = await _collection.Find(k => k.Id == keyId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            throw new InvalidOperationException($"API key with ID '{keyId}' not found.");
        }

        var name = entry.Name;

        // Revoke old key (bypass lockout check since we're creating a replacement)
        var revokeUpdate = Builders<ApiKeyEntry>.Update
            .Set(k => k.IsRevoked, true)
            .Set(k => k.RevokedAt, DateTime.UtcNow);

        await _collection.UpdateOneAsync(k => k.Id == keyId, revokeUpdate, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Create replacement
        var newKey = await CreateKeyAsync(name, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Regenerated API key '{Name}' — old key revoked, new key created", name);

        return newKey;
    }

    public async Task<int> GetActiveKeyCountAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var count = await _collection
            .CountDocumentsAsync(
                k => !k.IsRevoked && (!k.ExpiresAt.HasValue || k.ExpiresAt > now),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (int)count;
    }

    public async Task<bool> HasAnyKeysAsync(CancellationToken cancellationToken = default)
    {
        var count = await _collection
            .CountDocumentsAsync(FilterDefinition<ApiKeyEntry>.Empty, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return count > 0;
    }

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

    private void EnsureIndexes()
    {
        try
        {
            var indexModels = new[]
            {
                new CreateIndexModel<ApiKeyEntry>(
                    Builders<ApiKeyEntry>.IndexKeys.Ascending(k => k.KeyHash),
                    new CreateIndexOptions { Unique = true, Name = "idx_keyHash" }),
                new CreateIndexModel<ApiKeyEntry>(
                    Builders<ApiKeyEntry>.IndexKeys.Ascending(k => k.IsRevoked),
                    new CreateIndexOptions { Name = "idx_isRevoked" }),
            };

            _collection.Indexes.CreateMany(indexModels);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create API key indexes — they may already exist");
        }
    }
}
