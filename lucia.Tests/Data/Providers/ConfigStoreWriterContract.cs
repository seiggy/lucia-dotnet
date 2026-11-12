using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.PluginFramework;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Theory]
    [InlineData("Weather_API:", "WeatherXAPI:")]
    [InlineData("Weather%API:", "WeatherXYZAPI:")]
    [InlineData(@"Weather\API:", "WeatherAPI:")]
    public async Task DeleteByKeyPrefixAsync_TreatsWildcardCharactersLiterally(string prefix, string unrelatedPrefix)
    {
        await using var scope = await CreateAsync();
        await scope.Store.SetAsync(prefix + "ApiKey", "plugin", cancellationToken: CancellationToken.None);
        await scope.Store.SetAsync(unrelatedPrefix + "ApiKey", "other", cancellationToken: CancellationToken.None);

        var deleted = await scope.Store.DeleteByKeyPrefixAsync(prefix, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Null(await scope.Store.GetAsync(prefix + "ApiKey", CancellationToken.None));
        Assert.Equal("other", await scope.Store.GetAsync(unrelatedPrefix + "ApiKey", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteByKeysAsync_DeletesOnlyExactKeys()
    {
        await using var scope = await CreateAsync();
        await scope.Store.SetAsync("Weather:ApiKey", "first");
        await scope.Store.SetAsync("weather:ApiKey", "second");
        await scope.Store.SetAsync("Weather:ApiKeyExtra", "third");

        Assert.Equal(2, await scope.Store.DeleteByKeysAsync(["Weather:ApiKey", "weather:ApiKey"]));
        Assert.Null(await scope.Store.GetAsync("Weather:ApiKey"));
        Assert.Null(await scope.Store.GetAsync("weather:ApiKey"));
        Assert.Equal("third", await scope.Store.GetAsync("Weather:ApiKeyExtra"));
        Assert.Equal(0, await scope.Store.DeleteByKeysAsync([]));
    }

    [Theory]
    [InlineData("Weather", "Weather:Radar")]
    [InlineData("Weather:Radar", "Weather")]
    [InlineData("Shared", "shared")]
    public async Task Uninstall_SharedOrNestedSectionsSurviveRestart(string firstSection, string secondSection)
    {
        await using var scope = await CreateAsync();
        var pluginDir = Path.Combine(Path.GetTempPath(), "lucia-plugin-config-" + Guid.NewGuid().ToString("N"));
        var first = A.Fake<ILuciaPlugin>();
        A.CallTo(() => first.PluginId).Returns("first");
        A.CallTo(() => first.ConfigSection).Returns(firstSection);
        var second = A.Fake<ILuciaPlugin>();
        A.CallTo(() => second.PluginId).Returns("second");
        A.CallTo(() => second.ConfigSection).Returns(secondSection);
        var firstPath = Path.Combine(pluginDir, "first");
        var secondPath = Path.Combine(pluginDir, "second");
        Directory.CreateDirectory(firstPath);
        Directory.CreateDirectory(secondPath);
        await File.WriteAllTextAsync(Path.Combine(firstPath, "plugin.cs"), "");
        await File.WriteAllTextAsync(Path.Combine(secondPath, "plugin.cs"), "");
        await scope.Store.SetAsync(firstSection + ":ApiKey", "first", cancellationToken: CancellationToken.None);
        await scope.Store.SetAsync(secondSection + ":OtherKey", "second", cancellationToken: CancellationToken.None);
        await scope.Store.SetAsync(secondSection + ":ApiKey", "second-api", cancellationToken: CancellationToken.None);
        var repository = A.Fake<IPluginManagementRepository>();
        try
        {
            var beforeRestart = new PluginManagementService(repository, new PluginChangeTracker(), [],
                NullLogger<PluginManagementService>.Instance, pluginDir, scope.Store, [first, second]);
            await beforeRestart.UninstallPluginAsync("first");

            Assert.Equal("second", await scope.Store.GetAsync(secondSection + ":OtherKey"));
            Assert.Equal("second-api", await scope.Store.GetAsync(secondSection + ":ApiKey"));
            if (!string.Equals(firstSection, secondSection, StringComparison.OrdinalIgnoreCase)
                && !firstSection.StartsWith(secondSection + ":", StringComparison.OrdinalIgnoreCase))
                Assert.Null(await scope.Store.GetAsync(firstSection + ":ApiKey"));

            var afterRestart = new PluginManagementService(repository, new PluginChangeTracker(), [],
                NullLogger<PluginManagementService>.Instance, pluginDir, scope.Store, [second]);
            await afterRestart.UninstallPluginAsync("second");

            Assert.Null(await scope.Store.GetAsync(firstSection + ":ApiKey"));
            Assert.Null(await scope.Store.GetAsync(secondSection + ":OtherKey"));
            Assert.Null(await scope.Store.GetAsync(secondSection + ":ApiKey"));
        }
        finally
        {
            if (Directory.Exists(pluginDir))
                Directory.Delete(pluginDir, recursive: true);
        }
    }

    [Fact]
    public async Task Uninstall_ConcurrentSharedOwners_RemovesConfigAfterBothUninstalls()
    {
        await using var scope = await CreateAsync();
        var pluginDir = Path.Combine(Path.GetTempPath(), "lucia-plugin-concurrent-" + Guid.NewGuid().ToString("N"));
        var first = A.Fake<ILuciaPlugin>();
        A.CallTo(() => first.PluginId).Returns("first");
        A.CallTo(() => first.ConfigSection).Returns("Shared");
        var second = A.Fake<ILuciaPlugin>();
        A.CallTo(() => second.PluginId).Returns("second");
        A.CallTo(() => second.ConfigSection).Returns("Shared");
        foreach (var id in new[] { "first", "second" })
        {
            var path = Path.Combine(pluginDir, id);
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "plugin.cs"), "");
        }

        await scope.Store.SetAsync("Shared:ApiKey", "secret");
        try
        {
            var service = new PluginManagementService(A.Fake<IPluginManagementRepository>(), new PluginChangeTracker(),
                [], NullLogger<PluginManagementService>.Instance, pluginDir, scope.Store, [first, second]);
            await Task.WhenAll(service.UninstallPluginAsync("first"), service.UninstallPluginAsync("second"));
            Assert.Null(await scope.Store.GetAsync("Shared:ApiKey"));
        }
        finally
        {
            if (Directory.Exists(pluginDir))
                Directory.Delete(pluginDir, recursive: true);
        }
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
