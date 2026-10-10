using lucia.Agents.Abstractions;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Behaviour every <see cref="IMemoryStore"/> provider must share. Reserved-history filtering,
/// wildcard literals and absolute deadlines stay in the existing per-provider tests.
/// </summary>
public abstract class MemoryStoreContract
{
    protected abstract Task<StoreScope<IMemoryStore>> CreateAsync();

    [Fact]
    public async Task RetrieveAsync_MissingKey_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.RetrieveAsync("user-1", "nothing"));
    }

    [Fact]
    public async Task StoreAsync_ThenRetrieveAsync_RoundTrips()
    {
        await using var scope = await CreateAsync();

        await scope.Store.StoreAsync("user-1", "timezone", "America/Chicago");

        Assert.Equal("America/Chicago", await scope.Store.RetrieveAsync("user-1", "timezone"));
    }

    [Fact]
    public async Task StoreAsync_SameKey_ReplacesValue()
    {
        await using var scope = await CreateAsync();
        await scope.Store.StoreAsync("user-1", "diet", "vegetarian");

        await scope.Store.StoreAsync("user-1", "diet", "vegan");

        Assert.Equal("vegan", await scope.Store.RetrieveAsync("user-1", "diet"));
        Assert.Single(await scope.Store.GetAllAsync("user-1"));
    }

    [Fact]
    public async Task StoreAsync_SameKeyDifferentUsers_AreIsolated()
    {
        await using var scope = await CreateAsync();

        await scope.Store.StoreAsync("user-1", "color", "red");
        await scope.Store.StoreAsync("user-2", "color", "blue");

        Assert.Equal("red", await scope.Store.RetrieveAsync("user-1", "color"));
        Assert.Equal("blue", await scope.Store.RetrieveAsync("user-2", "color"));
        Assert.Empty(await scope.Store.GetAllAsync("user-3"));
    }

    [Fact]
    public async Task StoreAsync_ElapsedTtl_HidesEntryFromEveryRead()
    {
        await using var scope = await CreateAsync();
        await scope.Store.StoreAsync("user-1", "stale", "gone", DateTimeOffset.UtcNow.AddMinutes(-1));
        await scope.Store.StoreAsync("user-1", "fresh", "here", TimeSpan.FromHours(1));

        Assert.Null(await scope.Store.RetrieveAsync("user-1", "stale"));
        Assert.Equal("here", await scope.Store.RetrieveAsync("user-1", "fresh"));
        Assert.DoesNotContain(await scope.Store.GetAllAsync("user-1"), entry => entry.Key == "stale");
        Assert.DoesNotContain(await scope.Store.SearchAsync("user-1"), entry => entry.Key == "stale");
    }

    [Fact]
    public async Task SearchAsync_MatchesKeyOrValueCaseInsensitively()
    {
        await using var scope = await CreateAsync();
        await scope.Store.StoreAsync("user-1", "diet", "Vegetarian");
        await scope.Store.StoreAsync("user-1", "pet", "dog named Rex");

        var byValue = await scope.Store.SearchAsync("user-1", "vegetarian");
        var byKey = await scope.Store.SearchAsync("user-1", "PET");

        Assert.Equal("diet", Assert.Single(byValue).Key);
        Assert.Equal("pet", Assert.Single(byKey).Key);
    }

    [Fact]
    public async Task SearchAsync_HonoursLimit()
    {
        await using var scope = await CreateAsync();
        for (var i = 0; i < 5; i++)
        {
            await scope.Store.StoreAsync("user-1", $"key-{i}", $"value-{i}");
        }

        Assert.Equal(2, (await scope.Store.SearchAsync("user-1", limit: 2)).Count);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheNamedEntry()
    {
        await using var scope = await CreateAsync();
        await scope.Store.StoreAsync("user-1", "a", "1");
        await scope.Store.StoreAsync("user-1", "b", "2");

        await scope.Store.DeleteAsync("user-1", "a");

        Assert.Null(await scope.Store.RetrieveAsync("user-1", "a"));
        Assert.Equal("2", await scope.Store.RetrieveAsync("user-1", "b"));
    }

    [Fact]
    public async Task DeleteAsync_MissingKey_DoesNotThrow()
    {
        await using var scope = await CreateAsync();

        await scope.Store.DeleteAsync("user-1", "never-stored");
    }
}
