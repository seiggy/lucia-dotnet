using lucia.Data.InMemory;

namespace lucia.Tests.Data;

public sealed class InMemoryMemoryStoreTests
{
    [Fact]
    public async Task StoreAsync_And_RetrieveAsync_RoundTrips()
    {
        var store = new InMemoryMemoryStore();

        await store.StoreAsync("user-1", "favorite-color", "blue");

        var value = await store.RetrieveAsync("user-1", "favorite-color");

        Assert.Equal("blue", value);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsNull_ForExpiredEntry()
    {
        var store = new InMemoryMemoryStore();

        await store.StoreAsync("user-1", "expired", "value", TimeSpan.FromMilliseconds(-1));

        var value = await store.RetrieveAsync("user-1", "expired");

        Assert.Null(value);
    }

    [Fact]
    public async Task SearchAsync_MatchesKeyAndValue_AndSkipsExpiredEntries()
    {
        var store = new InMemoryMemoryStore();

        await store.StoreAsync("user-1", "favorite-color", "blue");
        await store.StoreAsync("user-1", "music-style", "classic rock");
        await store.StoreAsync("user-1", "expired-entry", "do not return", TimeSpan.FromMilliseconds(-1));

        var results = await store.SearchAsync("user-1", "rock");
        var allEntries = await store.GetAllAsync("user-1");

        Assert.Single(results);
        Assert.Equal("music-style", results[0].Key);
        Assert.DoesNotContain(allEntries, entry => entry.Key == "expired-entry");
    }
}
