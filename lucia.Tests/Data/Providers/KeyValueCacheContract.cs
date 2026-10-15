using lucia.Agents.Abstractions;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IKeyValueCache"/> provider (in-memory, Redis) must share.
/// Keys are unique per test, so providers backed by a shared server stay isolated.
/// </summary>
public abstract class KeyValueCacheContract
{
    protected abstract IKeyValueCache CreateCache();

    private static string NewKey() => $"test:{Guid.NewGuid():N}";

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        Assert.Null(await CreateCache().GetAsync(NewKey()));
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsTheValue()
    {
        var cache = CreateCache();
        var key = NewKey();

        await cache.SetAsync(key, "{\"a\":1}", TimeSpan.FromMinutes(1));

        Assert.Equal("{\"a\":1}", await cache.GetAsync(key));
    }

    [Fact]
    public async Task SetAsync_SameKey_ReplacesTheValue()
    {
        var cache = CreateCache();
        var key = NewKey();
        await cache.SetAsync(key, "first", TimeSpan.FromMinutes(1));

        await cache.SetAsync(key, "second", TimeSpan.FromMinutes(1));

        Assert.Equal("second", await cache.GetAsync(key));
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyTheNamedKey()
    {
        var cache = CreateCache();
        var removed = NewKey();
        var kept = NewKey();
        await cache.SetAsync(removed, "a", TimeSpan.FromMinutes(1));
        await cache.SetAsync(kept, "b", TimeSpan.FromMinutes(1));

        await cache.RemoveAsync(removed);

        Assert.Null(await cache.GetAsync(removed));
        Assert.Equal("b", await cache.GetAsync(kept));
    }

    [Fact]
    public async Task RemoveAsync_MissingKey_DoesNotThrow()
    {
        await CreateCache().RemoveAsync(NewKey());
    }

    [Fact]
    public async Task SetAsync_ElapsedTimeToLive_ExpiresTheEntry()
    {
        var cache = CreateCache();
        var key = NewKey();

        await cache.SetAsync(key, "short-lived", TimeSpan.FromMilliseconds(150));
        await Task.Delay(600);

        Assert.Null(await cache.GetAsync(key));
    }
}