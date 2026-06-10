using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using Microsoft.Extensions.Logging;

namespace lucia.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="CachedApiKeyService"/>, focused on cache-invalidation
/// correctness under the <see cref="IApiKeyService.OverrideKeyFromPlaintextAsync"/> paths.
/// </summary>
public sealed class CachedApiKeyServiceTests
{
    private static readonly TimeSpan ShortCache = TimeSpan.FromMinutes(5);

    private readonly IApiKeyService _inner = A.Fake<IApiKeyService>();
    private readonly ILogger<CachedApiKeyService> _logger = A.Fake<ILogger<CachedApiKeyService>>();

    private CachedApiKeyService CreateService()
        => new(_inner, _logger, cacheDuration: ShortCache);

    // ── OverrideKeyFromPlaintext invalidation paths ──────────────────────

    [Fact]
    public async Task OverrideKeyFromPlaintext_WhenCreatedIsNotNull_InvalidatesCache()
    {
        // Arrange: prime a validation in the cache
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        var entry = MakeEntry("key-1");

        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(entry));

        var svc = CreateService();
        await svc.ValidateKeyAsync(plaintext);       // seeds the cache

        // Override returns a newly-created key (standard replace path)
        var created = new ApiKeyCreateResponse
        {
            Id = "new-1",
            Key = "lk_test_new_key_abcdef1234567890",
            Prefix = "lk_test",
            Name = "Dashboard",
            CreatedAt = DateTime.UtcNow
        };
        A.CallTo(() => _inner.OverrideKeyFromPlaintextAsync(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<(ApiKeyCreateResponse?, int)>((created, 1)));

        // Inner now returns null — the old key has been revoked
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(null));

        // Act
        await svc.OverrideKeyFromPlaintextAsync("Dashboard", plaintext);

        // Assert: subsequent validation goes through to inner (cache is cleared)
        var result = await svc.ValidateKeyAsync(plaintext);
        Assert.Null(result);

        // ValidateKeyAsync must have been called twice: once to prime, once after invalidation
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task OverrideKeyFromPlaintext_WhenCreatedIsNull_StillInvalidatesCache()
    {
        // Arrange: prime a valid validation in the cache
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        var entry = MakeEntry("key-2");

        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(entry));

        var svc = CreateService();
        await svc.ValidateKeyAsync(plaintext);       // seeds the cache

        // Override is a no-op at the inner layer (another instance already replaced the key)
        A.CallTo(() => _inner.OverrideKeyFromPlaintextAsync(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<(ApiKeyCreateResponse?, int)>((null, 0)));

        // Inner now returns null — the previously-valid key is no longer valid
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(null));

        // Act
        await svc.OverrideKeyFromPlaintextAsync("Dashboard", plaintext);

        // Assert: cache must be cleared even though Created was null
        var result = await svc.ValidateKeyAsync(plaintext);
        Assert.Null(result);

        // ValidateKeyAsync must have been called twice: once to prime, once after invalidation
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task OverrideKeyFromPlaintext_WhenCreatedIsNull_RevokeOnly_StillInvalidatesCache()
    {
        // Edge case: override revoked N keys but the insert was a duplicate (race).
        // Created == null, RevokedCount > 0. Cache must still be cleared.
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        var entry = MakeEntry("key-3");

        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(entry));

        var svc = CreateService();
        await svc.ValidateKeyAsync(plaintext);       // seeds the cache

        // Override revoked existing keys but the insert collided with another concurrent writer
        A.CallTo(() => _inner.OverrideKeyFromPlaintextAsync(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<(ApiKeyCreateResponse?, int)>((null, 2)));

        // Inner now returns null for the old key
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(null));

        await svc.OverrideKeyFromPlaintextAsync("Dashboard", plaintext);

        var result = await svc.ValidateKeyAsync(plaintext);
        Assert.Null(result);

        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static ApiKeyEntry MakeEntry(string id) =>
        new()
        {
            Id = id,
            KeyHash = "hash-" + id,
            KeyPrefix = "lk_test",
            Name = "Dashboard",
            Scopes = ["*"]
        };
}
