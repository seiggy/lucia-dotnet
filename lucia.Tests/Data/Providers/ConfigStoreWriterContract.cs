using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.Tests.Data.Providers;

public abstract class ConfigStoreWriterContract
{
    protected abstract Task<StoreScope<IConfigStoreWriter>> CreateAsync();

    [Fact]
    public async Task GetAsync_EmptyStore_ReturnsNullAndNoEntries()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await scope.Store.GetAsync("missing:key", CancellationToken.None));
        Assert.Equal(0, await scope.Store.GetEntryCountAsync(CancellationToken.None));
        Assert.Empty(await scope.Store.GetAllKeysAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_AndGetAsync_RoundTripValueAndMetadata()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SetAsync(
            "HomeAssistant:BaseUrl",
            "http://localhost:8123",
            updatedBy: "test-user",
            isSensitive: true,
            cancellationToken: CancellationToken.None);

        var value = await scope.Store.GetAsync("HomeAssistant:BaseUrl", CancellationToken.None);
        Assert.Equal("http://localhost:8123", value);

        var entry = Assert.Single(await scope.Store.GetAllEntriesAsync(CancellationToken.None));
        Assert.Equal("HomeAssistant:BaseUrl", entry.Key);
        Assert.Equal("HomeAssistant", entry.Section);
        Assert.Equal("http://localhost:8123", entry.Value);
        Assert.Equal("test-user", entry.UpdatedBy);
        Assert.True(entry.IsSensitive);
    }

    [Fact]
    public async Task SetAsync_UpsertsExistingKeyWithoutCreatingDuplicateEntry()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SetAsync("Feature:Flag", "true", cancellationToken: CancellationToken.None);
        await scope.Store.SetAsync("Feature:Flag", "false", updatedBy: "override-user", cancellationToken: CancellationToken.None);

        Assert.Equal(1, await scope.Store.GetEntryCountAsync(CancellationToken.None));
        Assert.Equal("false", await scope.Store.GetAsync("Feature:Flag", CancellationToken.None));

        var entry = Assert.Single(await scope.Store.GetAllEntriesAsync(CancellationToken.None));
        Assert.Equal("override-user", entry.UpdatedBy);
        Assert.Equal("Feature", entry.Section);
    }

    [Fact]
    public async Task InsertManyAsync_AndSectionOrPrefixQueries_Work()
    {
        await using var scope = await CreateAsync();

        await scope.Store.InsertManyAsync(
        [
            new ConfigEntry { Key = "Feature:Alpha", Value = "a", Section = "Feature", UpdatedAt = DateTime.UtcNow, UpdatedBy = "test", IsSensitive = false },
            new ConfigEntry { Key = "Feature:Beta", Value = "b", Section = "Feature", UpdatedAt = DateTime.UtcNow, UpdatedBy = "test", IsSensitive = false },
            new ConfigEntry { Key = "Other:Gamma", Value = "c", Section = "Other", UpdatedAt = DateTime.UtcNow, UpdatedBy = "test", IsSensitive = false },
        ],
            CancellationToken.None);

        var bySection = await scope.Store.GetEntriesBySectionAsync("Feature", CancellationToken.None);
        var byPrefix = await scope.Store.GetEntriesByKeyPrefixAsync("Feature", CancellationToken.None);

        Assert.Equal(2, bySection.Count);
        Assert.Equal(2, byPrefix.Count);
        Assert.Contains(bySection, e => e.Key == "Feature:Alpha");
        Assert.Contains(bySection, e => e.Key == "Feature:Beta");
    }

    [Fact]
    public async Task DeleteByKeyPrefixAsync_OnlyDeletesMatchingEntries()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SetAsync("Feature:Alpha", "a", cancellationToken: CancellationToken.None);
        await scope.Store.SetAsync("Feature:Beta", "b", cancellationToken: CancellationToken.None);
        await scope.Store.SetAsync("Other:Gamma", "c", cancellationToken: CancellationToken.None);

        var deleted = await scope.Store.DeleteByKeyPrefixAsync("Feature", CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Equal(1, await scope.Store.GetEntryCountAsync(CancellationToken.None));
        Assert.Equal("c", await scope.Store.GetAsync("Other:Gamma", CancellationToken.None));
        Assert.Null(await scope.Store.GetAsync("Feature:Alpha", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAllAsync_RemovesEverything()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SetAsync("A:One", "1", cancellationToken: CancellationToken.None);
        await scope.Store.SetAsync("B:Two", "2", cancellationToken: CancellationToken.None);

        var deleted = await scope.Store.DeleteAllAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Equal(0, await scope.Store.GetEntryCountAsync(CancellationToken.None));
        Assert.Empty(await scope.Store.GetAllKeysAsync(CancellationToken.None));
    }
}
