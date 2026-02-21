using System.Security.Cryptography;
using System.Text;
using FakeItEasy;
using lucia.Agents.Auth;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace lucia.Tests.Auth;

public class MongoApiKeyServiceTests
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<ApiKeyEntry> _collection;
    private readonly ILogger<MongoApiKeyService> _logger;
    private readonly MongoApiKeyService _service;

    public MongoApiKeyServiceTests()
    {
        _mongoClient = A.Fake<IMongoClient>();
        _database = A.Fake<IMongoDatabase>();
        _collection = A.Fake<IMongoCollection<ApiKeyEntry>>();
        _logger = A.Fake<ILogger<MongoApiKeyService>>();

        A.CallTo(() => _mongoClient.GetDatabase(A<string>._, A<MongoDatabaseSettings?>._))
            .Returns(_database);
        A.CallTo(() => _database.GetCollection<ApiKeyEntry>(A<string>._, A<MongoCollectionSettings?>._))
            .Returns(_collection);

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
    public async Task ValidateKeyAsync_ReturnsEntryForValidKey()
    {
        var plaintextKey = "lk_test-valid-key-abc123";
        var hash = ComputeSha256Hash(plaintextKey);
        var entry = new ApiKeyEntry
        {
            Id = "entry-1",
            KeyHash = hash,
            KeyPrefix = "lk_test-vali...",
            Name = "Valid Key",
            IsRevoked = false,
        };

        SetupFindAsync(entry);
        SetupUpdateOneAsync(1);

        var result = await _service.ValidateKeyAsync(plaintextKey);

        Assert.NotNull(result);
        Assert.Equal("entry-1", result.Id);
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
