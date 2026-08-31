using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace lucia.Agents.Auth;

/// <summary>
/// MongoDB-backed API key service. Stores SHA-256 hashes of keys, never plaintext.
/// </summary>
public sealed class MongoApiKeyService : IApiKeyService
{
    private const string MutationLockId = "api-key-mutations";
    private static readonly TimeSpan s_mutationLockLease =
        TimeSpan.FromMinutes(2);
    private readonly IMongoCollection<ApiKeyEntry> _collection;
    private readonly IMongoCollection<BsonDocument> _mutationLocks;
    private readonly ConcurrentDictionary<
        string,
        (CancellationTokenSource Stop, Task Renewal)> _lockRenewals = new();
    private readonly ILogger<MongoApiKeyService> _logger;

    public MongoApiKeyService(IMongoClient mongoClient, ILogger<MongoApiKeyService> logger)
    {
        var database = mongoClient.GetDatabase(ApiKeyEntry.DatabaseName);
        _collection = database.GetCollection<ApiKeyEntry>(ApiKeyEntry.CollectionName);
        _mutationLocks = database.GetCollection<BsonDocument>(
            "api_key_mutation_locks");
        _logger = logger;

        EnsureIndexes();
    }

    public async Task<ApiKeyCreateResponse> CreateKeyAsync(
        string name,
        CancellationToken cancellationToken = default,
        bool isAdministrator = false)
    {
        var plaintextKey = GenerateKey();
        var hash = HashKey(plaintextKey);
        var prefix = plaintextKey[..Math.Min(12, plaintextKey.Length)] + "...";

        var entry = new ApiKeyEntry
        {
            KeyHash = hash,
            KeyPrefix = prefix,
            Name = name,
            Scopes = ApiKeyScopes.Create(isAdministrator),
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

    public async Task<ApiKeyCreateResponse?> CreateAdministratorKeyIfNoneAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var lockOwner = await AcquireMutationLockAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var administratorCount = await _collection.CountDocumentsAsync(
                    key => !key.IsRevoked
                        && (!key.ExpiresAt.HasValue || key.ExpiresAt > now)
                        && (key.Name == name
                            || key.Scopes.Contains(
                                AuthOptions.AdministratorScope)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return administratorCount > 0
                ? null
                : await CreateKeyAsync(
                        name,
                        cancellationToken,
                        isAdministrator: true)
                    .ConfigureAwait(false);
        }
        finally
        {
            await ReleaseMutationLockAsync(lockOwner).ConfigureAwait(false);
        }
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
            Scopes = ApiKeyScopes.Create(isAdministrator: false),
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
        var lockOwner = await AcquireMutationLockAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var entry = await _collection.Find(k => k.Id == keyId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (entry is null || entry.IsRevoked)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            var isActive =
                !entry.ExpiresAt.HasValue || entry.ExpiresAt > now;
            if (isActive)
            {
                var activeCount = await GetActiveKeyCountAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
                if (activeCount <= 1)
                {
                    throw new InvalidOperationException("Cannot revoke the last active API key. Create a new key first.");
                }
            }
            if (isActive
                && entry.Scopes.Contains(
                    AuthOptions.AdministratorScope,
                    StringComparer.Ordinal))
            {
                var administratorCount = await _collection.CountDocumentsAsync(
                        key => !key.IsRevoked
                            && (!key.ExpiresAt.HasValue || key.ExpiresAt > now)
                            && key.Scopes.Contains(
                                AuthOptions.AdministratorScope),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (administratorCount <= 1)
                {
                    throw new InvalidOperationException(
                        "Cannot revoke the last active administrator key. Regenerate it instead.");
                }
            }

            var update = Builders<ApiKeyEntry>.Update
                .Set(k => k.IsRevoked, true)
                .Set(k => k.RevokedAt, DateTime.UtcNow);

            await RenewMutationLockAsync(lockOwner, cancellationToken)
                .ConfigureAwait(false);
            var result = await _collection
                .UpdateOneAsync(k => k.Id == keyId && !k.IsRevoked, update, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result.ModifiedCount > 0)
            {
                _logger.LogInformation("Revoked API key '{Name}' ({Prefix})", entry.Name, entry.KeyPrefix);
            }

            return result.ModifiedCount > 0;
        }
        finally
        {
            await ReleaseMutationLockAsync(lockOwner).ConfigureAwait(false);
        }
    }

    public async Task<ApiKeyCreateResponse> RegenerateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        var lockOwner = await AcquireMutationLockAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var entry = await _collection.Find(k => k.Id == keyId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (entry is null)
            {
                throw new InvalidOperationException($"API key with ID '{keyId}' not found.");
            }

            var name = entry.Name;

            var newKey = await CreateKeyAsync(
                    name,
                    cancellationToken,
                    entry.Scopes.Contains(
                        AuthOptions.AdministratorScope,
                        StringComparer.Ordinal))
                .ConfigureAwait(false);
            try
            {
                await RenewMutationLockAsync(lockOwner, cancellationToken)
                    .ConfigureAwait(false);
                var revokeUpdate = Builders<ApiKeyEntry>.Update
                    .Set(k => k.IsRevoked, true)
                    .Set(k => k.RevokedAt, DateTime.UtcNow);

                var revokeResult = await _collection.UpdateOneAsync(
                        k => k.Id == keyId,
                        revokeUpdate,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (revokeResult.ModifiedCount != 1)
                {
                    throw new InvalidOperationException(
                        $"API key with ID '{keyId}' could not be revoked.");
                }
            }
            catch
            {
                await RenewMutationLockAsync(
                        lockOwner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                await _collection.DeleteOneAsync(
                        key => key.Id == newKey.Id,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }

            _logger.LogInformation("Regenerated API key '{Name}' — old key revoked, new key created", name);

            return newKey;
        }
        finally
        {
            await ReleaseMutationLockAsync(lockOwner).ConfigureAwait(false);
        }
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

    public async Task<(ApiKeyCreateResponse? Created, int RevokedCount)> OverrideKeyFromPlaintextAsync(
        string name,
        string plaintextKey,
        CancellationToken cancellationToken = default,
        bool isAdministrator = false)
    {
        if (string.IsNullOrWhiteSpace(plaintextKey) || plaintextKey.Length < 16)
            return (null, 0);

        var lockOwner = await AcquireMutationLockAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var hash = HashKey(plaintextKey);
            var now = DateTime.UtcNow;
            var existingMatch = await _collection
                .Find(key =>
                    key.Name == name
                    && key.KeyHash == hash
                    && !key.IsRevoked
                    && (!key.ExpiresAt.HasValue || key.ExpiresAt > now))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (existingMatch is not null)
            {
                if (isAdministrator)
                {
                    await _collection.UpdateOneAsync(
                            key => key.Id == existingMatch.Id,
                            Builders<ApiKeyEntry>.Update.AddToSet(
                                key => key.Scopes,
                                AuthOptions.AdministratorScope),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                return (null, 0);
            }

            var existingHash = await _collection
                .Find(key => key.KeyHash == hash)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var prefix = plaintextKey.Length <= 12
                ? plaintextKey
                : plaintextKey[..12] + "...";
            var id = existingHash?.Id
                ?? ObjectId.GenerateNewId().ToString();
            var scopes = ApiKeyScopes.Create(isAdministrator);
            var replacement = await _collection.FindOneAndUpdateAsync(
                    key => key.KeyHash == hash,
                    Builders<ApiKeyEntry>.Update
                        .SetOnInsert(key => key.Id, id)
                        .Set(key => key.KeyPrefix, prefix)
                        .Set(key => key.Name, name)
                        .Set(key => key.Scopes, scopes)
                        .Set(key => key.CreatedAt, now)
                        .Set(key => key.LastUsedAt, null)
                        .Set(key => key.ExpiresAt, null)
                        .Set(key => key.IsRevoked, false)
                        .Set(key => key.RevokedAt, null),
                    new FindOneAndUpdateOptions<ApiKeyEntry>
                    {
                        IsUpsert = true,
                        ReturnDocument = ReturnDocument.After,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await RenewMutationLockAsync(lockOwner, cancellationToken)
                    .ConfigureAwait(false);
                var revokeResult = await _collection.UpdateManyAsync(
                        key => key.Name == name
                            && key.Id != replacement.Id
                            && !key.IsRevoked,
                        Builders<ApiKeyEntry>.Update
                            .Set(key => key.IsRevoked, true)
                            .Set(key => key.RevokedAt, now),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _logger.LogInformation("Override API key '{Name}' from env (prefix {Prefix})", name, prefix);
                return (new ApiKeyCreateResponse
                {
                    Key = plaintextKey,
                    Id = replacement.Id,
                    Prefix = prefix,
                    Name = name,
                    CreatedAt = now,
                }, (int)revokeResult.ModifiedCount);
            }
            catch
            {
                await RenewMutationLockAsync(
                        lockOwner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (existingHash is null)
                {
                    await _collection.DeleteOneAsync(
                            key => key.Id == replacement.Id,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _collection.ReplaceOneAsync(
                            key => key.Id == existingHash.Id,
                            existingHash,
                            cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            await ReleaseMutationLockAsync(lockOwner).ConfigureAwait(false);
        }
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

    private async Task<string> AcquireMutationLockAsync(
        CancellationToken cancellationToken)
    {
        var owner = Guid.NewGuid().ToString("N");
        while (true)
        {
            var now = DateTime.UtcNow;
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", MutationLockId),
                Builders<BsonDocument>.Filter.Lte("expiresAt", now));
            var update = Builders<BsonDocument>.Update
                .Set("owner", owner)
                .Set("expiresAt", now.Add(s_mutationLockLease));
            var acquired = await _mutationLocks.FindOneAndUpdateAsync(
                    filter,
                    update,
                    new FindOneAndUpdateOptions<BsonDocument>
                    {
                        ReturnDocument = ReturnDocument.After,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (acquired is not null)
            {
                return StartMutationLockRenewal(owner);
            }

            try
            {
                await _mutationLocks.InsertOneAsync(
                        new BsonDocument
                        {
                            ["_id"] = MutationLockId,
                            ["owner"] = owner,
                            ["expiresAt"] = now.Add(s_mutationLockLease),
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return StartMutationLockRenewal(owner);
            }
            catch (MongoWriteException exception) when (
                exception.WriteError.Category
                    == ServerErrorCategory.DuplicateKey)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(50),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ReleaseMutationLockAsync(string owner)
    {
        if (_lockRenewals.TryRemove(owner, out var renewal))
        {
            renewal.Stop.Cancel();
            try
            {
                await renewal.Renewal.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                renewal.Stop.IsCancellationRequested)
            {
                _logger.LogDebug(
                    "API key mutation lock renewal stopped for release.");
            }
            catch (Exception exception) when (
                exception is MongoException
                    or InvalidOperationException)
            {
                _logger.LogWarning(
                    exception,
                    "API key mutation lock renewal stopped unexpectedly.");
            }
            renewal.Stop.Dispose();
        }

        try
        {
            await _mutationLocks.DeleteOneAsync(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq(
                            "_id",
                            MutationLockId),
                        Builders<BsonDocument>.Filter.Eq("owner", owner)),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (MongoException exception)
        {
            _logger.LogWarning(
                exception,
                "API key mutation lock release failed; its lease will expire.");
        }
    }

    private string StartMutationLockRenewal(string owner)
    {
        var stop = new CancellationTokenSource();
        if (!_lockRenewals.TryAdd(
                owner,
                (stop, RenewMutationLockUntilCanceledAsync(
                    owner,
                    stop.Token))))
        {
            stop.Dispose();
            throw new InvalidOperationException(
                "API key mutation lock renewal could not start.");
        }
        return owner;
    }

    private async Task RenewMutationLockUntilCanceledAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        var renewalInterval = TimeSpan.FromTicks(
            s_mutationLockLease.Ticks / 3);
        while (true)
        {
            await Task.Delay(renewalInterval, cancellationToken)
                .ConfigureAwait(false);
            await RenewMutationLockAsync(owner, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RenewMutationLockAsync(
        string owner,
        CancellationToken cancellationToken)
    {
        var result = await _mutationLocks.UpdateOneAsync(
                Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq(
                        "_id",
                        MutationLockId),
                    Builders<BsonDocument>.Filter.Eq("owner", owner)),
                Builders<BsonDocument>.Update.Set(
                    "expiresAt",
                    DateTime.UtcNow.Add(s_mutationLockLease)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result.MatchedCount != 1)
        {
            throw new InvalidOperationException(
                "API key mutation lock ownership was lost.");
        }
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
