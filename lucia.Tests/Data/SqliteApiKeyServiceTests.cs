using FakeItEasy;
using lucia.Agents.Auth;
using lucia.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Data;

public sealed class SqliteApiKeyServiceTests : IDisposable
{
    private readonly SqliteTestHelper _helper;
    private readonly SqliteApiKeyService _service;

    public SqliteApiKeyServiceTests()
    {
        _helper = new SqliteTestHelper();
        var logger = A.Fake<ILogger<SqliteApiKeyService>>();
        _service = new SqliteApiKeyService(_helper.ConnectionFactory, logger);
    }

    [Fact]
    public async Task CreateKeyAsync_ReturnsKeyWithLkPrefix()
    {
        var result = await _service.CreateKeyAsync("test-key");

        Assert.StartsWith(AuthOptions.KeyPrefix, result.Key);
        Assert.False(string.IsNullOrWhiteSpace(result.Id));
        Assert.Equal("test-key", result.Name);
    }

    [Fact]
    public async Task ValidateKeyAsync_ValidatesCreatedKey()
    {
        var created = await _service.CreateKeyAsync("validate-key");

        var entry = await _service.ValidateKeyAsync(created.Key);

        Assert.NotNull(entry);
        Assert.Equal(created.Id, entry.Id);
        Assert.Equal("validate-key", entry.Name);
    }

    [Fact]
    public async Task ValidateKeyAsync_ReturnsNull_ForInvalidKey()
    {
        var entry = await _service.ValidateKeyAsync("lk_invalid_key_that_does_not_exist");

        Assert.Null(entry);
    }

    [Fact]
    public async Task ValidateKeyAsync_ReturnsNull_ForEmptyKey()
    {
        var entry = await _service.ValidateKeyAsync("");

        Assert.Null(entry);
    }

    [Fact]
    public async Task RevokeKeyAsync_RevokesKey()
    {
        // Need at least 2 keys so we can revoke one (lockout prevention)
        var key1 = await _service.CreateKeyAsync("key-to-keep");
        var key2 = await _service.CreateKeyAsync("key-to-revoke");

        var revoked = await _service.RevokeKeyAsync(key2.Id);

        Assert.True(revoked);

        var validateResult = await _service.ValidateKeyAsync(key2.Key);
        Assert.Null(validateResult);
    }

    [Fact]
    public async Task RevokeKeyAsync_ReturnsFalse_ForNonexistentKey()
    {
        var result = await _service.RevokeKeyAsync("nonexistent-id");

        Assert.False(result);
    }

    [Fact]
    public async Task RevokeKeyAsync_ThrowsWhenRevokingLastActiveKey()
    {
        var onlyKey = await _service.CreateKeyAsync("only-key");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RevokeKeyAsync(onlyKey.Id));
    }

    [Fact]
    public async Task ListKeysAsync_ListsAllKeys()
    {
        await _service.CreateKeyAsync("list-key-1");
        await _service.CreateKeyAsync("list-key-2");

        var keys = await _service.ListKeysAsync();

        Assert.Equal(2, keys.Count);
        Assert.Contains(keys, k => k.Name == "list-key-1");
        Assert.Contains(keys, k => k.Name == "list-key-2");
    }

    [Fact]
    public async Task HasAnyKeysAsync_ReturnsFalse_WhenEmpty()
    {
        var result = await _service.HasAnyKeysAsync();

        Assert.False(result);
    }

    [Fact]
    public async Task HasAnyKeysAsync_ReturnsTrue_AfterCreatingKey()
    {
        await _service.CreateKeyAsync("existence-check");

        var result = await _service.HasAnyKeysAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task GetActiveKeyCountAsync_ReturnsCorrectCount()
    {
        await _service.CreateKeyAsync("active-1");
        await _service.CreateKeyAsync("active-2");

        var count = await _service.GetActiveKeyCountAsync();

        Assert.Equal(2, count);
    }

    public void Dispose()
    {
        _helper.Dispose();
    }
}
