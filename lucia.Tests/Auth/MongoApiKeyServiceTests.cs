using System.Security.Cryptography;
using System.Text;
using FakeItEasy;
using lucia.Agents.Auth;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace lucia.Tests.Auth;

public class MongoApiKeyServiceTests
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<ApiKeyEntry> _collection;
    private readonly IMongoCollection<BsonDocument> _mutationLocks;
    private readonly ILogger<MongoApiKeyService> _logger;
    private readonly MongoApiKeyService _service;

    public MongoApiKeyServiceTests()
    {
        _mongoClient = A.Fake<IMongoClient>();
        _database = A.Fake<IMongoDatabase>();
        _collection = A.Fake<IMongoCollection<ApiKeyEntry>>();
        _mutationLocks = A.Fake<IMongoCollection<BsonDocument>>();
        _logger = A.Fake<ILogger<MongoApiKeyService>>();

        A.CallTo(() => _mongoClient.GetDatabase(A<string>._, A<MongoDatabaseSettings?>._))
            .Returns(_database);
        A.CallTo(() => _database.GetCollection<ApiKeyEntry>(A<string>._, A<MongoCollectionSettings?>._))
            .Returns(_collection);
        A.CallTo(() => _database.GetCollection<BsonDocument>(
                A<string>._,
                A<MongoCollectionSettings?>._))
            .Returns(_mutationLocks);
        A.CallTo(_mutationLocks)
            .Where(call => call.Method.Name == "FindOneAndUpdateAsync")
            .WithReturnType<Task<BsonDocument>>()
            .Returns(Task.FromResult<BsonDocument>(null!));
        A.CallTo(_mutationLocks)
            .Where(call => call.Method.Name == "InsertOneAsync")
            .WithReturnType<Task>()
            .Returns(Task.CompletedTask);

        _service = new MongoApiKeyService(_mongoClient, _logger);
    }

    [Fact]
    public async Task CreateKeyAsync_ReturnsKeyWithLkPrefix()
    {
        var result = await _service.CreateKeyAsync("test-key");

        Assert.StartsWith(AuthOptions.KeyPrefix, result.Key);
    }

    [Fact]
    public async Task CreateKeyAsync_StoresSha256HashNotPlaintext()
    {
        ApiKeyEntry? captured = null;
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "InsertOneAsync")
            .WithReturnType<Task>()
            .Invokes(call => captured = call.GetArgument<ApiKeyEntry>(0))
            .Returns(Task.CompletedTask);

        var result = await _service.CreateKeyAsync("test-key");

        Assert.NotNull(captured);
        // Stored value must be the SHA-256 hash, never the plaintext key
        Assert.NotEqual(result.Key, captured.KeyHash);
        var expectedHash = ComputeSha256Hash(result.Key);
        Assert.Equal(expectedHash, captured.KeyHash);
    }

    [Fact]
    public async Task ValidateKeyAsync_ReturnsEntryForValidKeyWithoutWriting()
    {
        var plaintextKey = "lk_test-valid-key-abc123";
        var hash = ComputeSha256Hash(plaintextKey);
        var lastUsedAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var entry = new ApiKeyEntry
        {
            Id = "entry-1",
            KeyHash = hash,
            KeyPrefix = "lk_test-vali...",
            Name = "Valid Key",
            IsRevoked = false,
            LastUsedAt = lastUsedAt,
            Scopes = ["read:test"],
        };

        SetupFindAsync(entry);

        var result = await _service.ValidateKeyAsync(plaintextKey);

        Assert.NotNull(result);
        Assert.Equal("entry-1", result.Id);
        Assert.Equal(lastUsedAt, result.LastUsedAt);
        Assert.Equal(["read:test"], result.Scopes);
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "UpdateOneAsync")
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ValidateKeyAsync_ReturnsNullForInvalidKey()
    {
        SetupFindAsync(null);

        var result = await _service.ValidateKeyAsync("lk_invalid-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateKeyAsync_ReturnsNullForRevokedKey()
    {
        // The MongoDB query filters by !IsRevoked, so a revoked key returns no results
        SetupFindAsync(null);

        var result = await _service.ValidateKeyAsync("lk_revoked-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateKeyAsync_ReturnsNullForExpiredKey()
    {
        var plaintextKey = "lk_expired-key-xyz789";
        var hash = ComputeSha256Hash(plaintextKey);
        var entry = new ApiKeyEntry
        {
            Id = "entry-expired",
            KeyHash = hash,
            KeyPrefix = "lk_expired-k...",
            Name = "Expired Key",
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddHours(-1),
        };

        SetupFindAsync(entry);

        var result = await _service.ValidateKeyAsync(plaintextKey);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeKeyAsync_ThrowsWhenRevokingLastActiveKey()
    {
        var keyId = "last-key";
        var entry = new ApiKeyEntry
        {
            Id = keyId,
            KeyHash = "some-hash",
            KeyPrefix = "lk_last-key...",
            Name = "Last Key",
            IsRevoked = false,
        };

        SetupCountDocumentsAsync(1);
        SetupFindAsync(entry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevokeKeyAsync(keyId));
    }

    [Fact]
    public async Task RevokeKeyAsync_ThrowsWhenRevokingLastAdministratorKey()
    {
        const string KeyId = "last-admin";
        var entry = new ApiKeyEntry
        {
            Id = KeyId,
            KeyHash = "admin-hash",
            KeyPrefix = "lk_admin...",
            Name = "Owner",
            IsRevoked = false,
            Scopes = ["*", AuthOptions.AdministratorScope],
        };
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "CountDocumentsAsync")
            .WithReturnType<Task<long>>()
            .ReturnsNextFromSequence(
                Task.FromResult(2L),
                Task.FromResult(1L));
        SetupFindAsync(entry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevokeKeyAsync(KeyId));
    }

    [Fact]
    public async Task RevokeKeyAsync_AllowsExpiredAdministratorKey()
    {
        const string KeyId = "expired-admin";
        SetupFindAsync(new ApiKeyEntry
        {
            Id = KeyId,
            KeyHash = "expired-hash",
            KeyPrefix = "lk_expired...",
            Name = "Expired owner",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            Scopes = ["*", AuthOptions.AdministratorScope],
        });
        SetupUpdateOneAsync(1);

        Assert.True(await _service.RevokeKeyAsync(KeyId));
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "CountDocumentsAsync")
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task RegenerateKeyAsync_RevokesOldAndCreatesNew()
    {
        var oldKeyId = "old-key";
        var oldEntry = new ApiKeyEntry
        {
            Id = oldKeyId,
            KeyHash = "old-hash",
            KeyPrefix = "lk_old-key...",
            Name = "My Key",
            IsRevoked = false,
        };

        SetupFindAsync(oldEntry);
        SetupUpdateOneAsync(1);

        var result = await _service.RegenerateKeyAsync(oldKeyId);

        Assert.NotNull(result);
        Assert.StartsWith(AuthOptions.KeyPrefix, result.Key);
        Assert.Equal("My Key", result.Name);

        // Verify the old key was revoked via UpdateOneAsync
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "UpdateOneAsync")
            .MustHaveHappened();
    }

    [Fact]
    public async Task RegenerateKeyAsync_InsertFailure_DoesNotRevokeOldKey()
    {
        var entry = new ApiKeyEntry
        {
            Id = "owner",
            KeyHash = "owner-hash",
            KeyPrefix = "lk_owner...",
            Name = "Owner",
            Scopes = ["*", AuthOptions.AdministratorScope],
        };
        SetupFindAsync(entry);
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "InsertOneAsync")
            .WithReturnType<Task>()
            .Returns(Task.FromException(
                new InvalidOperationException("simulated insert failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegenerateKeyAsync(entry.Id));

        A.CallTo(_collection)
            .Where(call => call.Method.Name == "UpdateOneAsync")
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task RegenerateKeyAsync_RevokeFailure_DeletesReplacement()
    {
        var entry = new ApiKeyEntry
        {
            Id = "owner",
            KeyHash = "owner-hash",
            KeyPrefix = "lk_owner...",
            Name = "Owner",
            Scopes = ["*", AuthOptions.AdministratorScope],
        };
        SetupFindAsync(entry);
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "UpdateOneAsync")
            .WithReturnType<Task<UpdateResult>>()
            .Returns(Task.FromException<UpdateResult>(
                new InvalidOperationException("simulated revoke failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RegenerateKeyAsync(entry.Id));

        A.CallTo(_collection)
            .Where(call => call.Method.Name == "DeleteOneAsync")
            .MustHaveHappenedOnceExactly();
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private void SetupFindAsync(ApiKeyEntry? result)
    {
        var cursor = A.Fake<IAsyncCursor<ApiKeyEntry>>();
        if (result is not null)
        {
            A.CallTo(() => cursor.MoveNextAsync(A<CancellationToken>._))
                .Returns(true).Once()
                .Then.Returns(false);
            A.CallTo(() => cursor.Current).Returns(new[] { result });
        }
        else
        {
            A.CallTo(() => cursor.MoveNextAsync(A<CancellationToken>._))
                .Returns(false);
        }

        A.CallTo(_collection)
            .Where(call => call.Method.Name == "FindAsync")
            .WithReturnType<Task<IAsyncCursor<ApiKeyEntry>>>()
            .Returns(Task.FromResult(cursor));
    }

    private void SetupCountDocumentsAsync(long count)
    {
        A.CallTo(_collection)
            .Where(call => call.Method.Name == "CountDocumentsAsync")
            .WithReturnType<Task<long>>()
            .Returns(Task.FromResult(count));
    }

    private void SetupUpdateOneAsync(long modifiedCount)
    {
        var updateResult = A.Fake<UpdateResult>();
        A.CallTo(() => updateResult.ModifiedCount).Returns(modifiedCount);

        A.CallTo(_collection)
            .Where(call => call.Method.Name == "UpdateOneAsync")
            .WithReturnType<Task<UpdateResult>>()
            .Returns(Task.FromResult(updateResult));
    }
}
