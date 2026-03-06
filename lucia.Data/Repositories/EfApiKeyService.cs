using System.Security.Cryptography;
using System.Text;

using lucia.Agents.Abstractions;
using lucia.Agents.Auth;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IApiKeyService"/>.
/// Stores SHA-256 hashes of keys, never plaintext.
/// </summary>
public sealed class EfApiKeyService(IDbContextFactory<LuciaDbContext> dbFactory, ILogger<EfApiKeyService> logger) : IApiKeyService
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;
    private readonly ILogger<EfApiKeyService> _logger = logger;

    public async Task<ApiKeyCreateResponse> CreateKeyAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey[..Math.Min(12, plaintextKey.Length)] + "...";

        var entry = new ApiKeyEntry
        {
            Id = ObjectId(),
            KeyHash = hash,
            KeyPrefix = prefix,
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };

        db.ApiKeys.Add(entry);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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

    public async Task<ApiKeyCreateResponse?> CreateKeyFromPlaintextAsync(
        string name,
        string plaintextKey,
        CancellationToken cancellationToken = default)
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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey.Length <= 12 ? plaintextKey : plaintextKey[..12] + "...";

        var entry = new ApiKeyEntry
        {
            Id = ObjectId(),
            KeyHash = hash,
            KeyPrefix = prefix,
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };

        db.ApiKeys.Add(entry);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hash = HashKey(plaintextKey);

        var entry = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == hash && !k.IsRevoked, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return null;
        }

        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
        {
            return null;
        }

        // Update last used timestamp
        entry.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        return entry;
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .Select(e => new ApiKeySummary
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
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RevokeKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        // Lockout prevention: don't revoke the last active key
        var activeCount = await GetActiveKeyCountAsync(cancellationToken).ConfigureAwait(false);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entry = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null || entry.IsRevoked)
        {
            return false;
        }

        if (activeCount <= 1)
        {
            throw new InvalidOperationException("Cannot revoke the last active API key. Create a new key first.");
        }

        entry.IsRevoked = true;
        entry.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Revoked API key '{Name}' ({Prefix})", entry.Name, entry.KeyPrefix);
        return true;
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entry = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            throw new InvalidOperationException($"API key with ID '{keyId}' not found.");
        }

        var name = entry.Name;

        // Revoke old key (bypass lockout check since we're creating a replacement)
        entry.IsRevoked = true;
        entry.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Create replacement
        var newKey = await CreateKeyAsync(name, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Regenerated API key '{Name}' — old key revoked, new key created", name);

        return newKey;
    }

    public async Task<int> GetActiveKeyCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        return await db.ApiKeys
            .CountAsync(
                k => !k.IsRevoked && (!k.ExpiresAt.HasValue || k.ExpiresAt > now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> HasAnyKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ApiKeys
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
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

    /// <summary>
    /// Generates a MongoDB-style ObjectId string for use as a primary key.
    /// </summary>
    private static string ObjectId() => Guid.NewGuid().ToString("N");
}
