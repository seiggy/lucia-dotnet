using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Data.InMemory;
using Microsoft.Extensions.Caching.Memory;
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
        => new(_inner, new InMemoryKeyValueCache(new MemoryCache(new MemoryCacheOptions())), _logger, cacheDuration: ShortCache);

    private sealed class GenerationWriteFailsCache(IKeyValueCache inner, Exception? failure = null) : IKeyValueCache
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => inner.GetAsync(key, cancellationToken);

        public Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
            => key.EndsWith("generation", StringComparison.Ordinal)
                ? throw (failure ?? new InvalidOperationException("cache is read-only"))
                : inner.SetAsync(key, value, timeToLive, cancellationToken);

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
            => inner.RemoveAsync(key, cancellationToken);
    }

    [Fact]
    public async Task RevokeKeyAsync_WhenInvalidationFails_StopsServingCachedKeys()
    {
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(MakeEntry("key-1")));
        A.CallTo(() => _inner.RevokeKeyAsync("key-1", A<CancellationToken>._)).Returns(true);
        var svc = new CachedApiKeyService(
            _inner,
            new GenerationWriteFailsCache(new InMemoryKeyValueCache(new MemoryCache(new MemoryCacheOptions()))),
            _logger,
            cacheDuration: ShortCache);
        Assert.NotNull(await svc.ValidateKeyAsync(plaintext));

        await svc.RevokeKeyAsync("key-1");
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(null));

        Assert.Null(await svc.ValidateKeyAsync(plaintext));
    }

    [Fact]
    public async Task RevokeKeyAsync_WhenInvalidationTimesOut_StillRevokes()
    {
        A.CallTo(() => _inner.RevokeKeyAsync("key-1", A<CancellationToken>._)).Returns(true);
        var svc = new CachedApiKeyService(
            _inner,
            new GenerationWriteFailsCache(
                new InMemoryKeyValueCache(new MemoryCache(new MemoryCacheOptions())),
                new TaskCanceledException("timed out", new TimeoutException())),
            _logger,
            cacheDuration: ShortCache);

        Assert.True(await svc.RevokeKeyAsync("key-1"));
    }

    [Fact]
    public async Task CreateKeyAsync_DoesNotInvalidateTheCache()
    {
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(MakeEntry("key-1")));
        var svc = CreateService();
        await svc.ValidateKeyAsync(plaintext);

        await svc.CreateKeyAsync("another");
        await svc.ValidateKeyAsync(plaintext);

        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    // ── Caching behaviour ────────────────────────────────────────────────

    [Fact]
    public async Task ValidateKeyAsync_RepeatedValidKey_HitsStoreOnce()
    {
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(MakeEntry("key-1")));
        var svc = CreateService();

        var first = await svc.ValidateKeyAsync(plaintext);
        var second = await svc.ValidateKeyAsync(plaintext);

        Assert.Equal("key-1", first?.Id);
        Assert.Equal("key-1", second?.Id);
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ValidateKeyAsync_InvalidKey_IsNotCached()
    {
        const string plaintext = "lk_unknown_key_abcdef1234567890ab";
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(null));
        var svc = CreateService();

        Assert.Null(await svc.ValidateKeyAsync(plaintext));
        Assert.Null(await svc.ValidateKeyAsync(plaintext));

        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Fact]
    public async Task RevokeKeyAsync_InvalidatesCachedValidation()
    {
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(MakeEntry("key-1")));
        A.CallTo(() => _inner.RevokeKeyAsync("key-1", A<CancellationToken>._)).Returns(true);
        var svc = CreateService();
        await svc.ValidateKeyAsync(plaintext);

        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(null));
        await svc.RevokeKeyAsync("key-1");

        Assert.Null(await svc.ValidateKeyAsync(plaintext));
    }

    [Fact]
    public async Task ValidateKeyAsync_CacheThrows_FallsBackToStore()
    {
        const string plaintext = "lk_test_key_abcdef1234567890abcdef";
        var cache = A.Fake<IKeyValueCache>();
        A.CallTo(() => cache.GetAsync(A<string>._, A<CancellationToken>._)).ThrowsAsync(new InvalidOperationException("cache down"));
        A.CallTo(() => _inner.ValidateKeyAsync(plaintext, A<CancellationToken>._))
            .Returns(Task.FromResult<ApiKeyEntry?>(MakeEntry("key-1")));
        var svc = new CachedApiKeyService(_inner, cache, _logger, ShortCache);

        Assert.Equal("key-1", (await svc.ValidateKeyAsync(plaintext))?.Id);
    }

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
        A.CallTo(() => _inner.OverrideKeyFromPlaintextAsync(
                A<string>._,
                A<string>._,
                A<CancellationToken>._))
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
        A.CallTo(() => _inner.OverrideKeyFromPlaintextAsync(
                A<string>._,
                A<string>._,
                A<CancellationToken>._))
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
        A.CallTo(() => _inner.OverrideKeyFromPlaintextAsync(
                A<string>._,
                A<string>._,
                A<CancellationToken>._))
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
