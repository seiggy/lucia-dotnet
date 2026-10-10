using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;

namespace lucia.Tests.Data.Providers;

public abstract class VisibilityConfigStoreContract
{
    protected abstract Task<StoreScope<IConfigStoreWriter>> CreateAsync();

    [Fact]
    public async Task LoadAsync_NothingSaved_ReturnsNull()
    {
        await using var scope = await CreateAsync();

        Assert.Null(await new ConfigStoreVisibilityConfigStore(scope.Store).LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsFromAnotherInstance()
    {
        await using var scope = await CreateAsync();
        var config = new EntityVisibilityConfig { UseExposedEntitiesOnly = true };
        config.EntityAgentMap["light.kitchen"] = ["light-agent", "scene-agent"];
        config.EntityAgentMap["light.porch"] = [];

        await new ConfigStoreVisibilityConfigStore(scope.Store).SaveAsync(config);
        var loaded = await new ConfigStoreVisibilityConfigStore(scope.Store).LoadAsync();

        Assert.NotNull(loaded);
        Assert.True(loaded.UseExposedEntitiesOnly);
        Assert.Equal(["light-agent", "scene-agent"], loaded.EntityAgentMap["light.kitchen"]);
        Assert.Empty(loaded.EntityAgentMap["light.porch"]);
    }

    [Fact]
    public async Task SaveAsync_Twice_KeepsTheLatest()
    {
        await using var scope = await CreateAsync();
        var store = new ConfigStoreVisibilityConfigStore(scope.Store);
        await store.SaveAsync(new EntityVisibilityConfig { UseExposedEntitiesOnly = true });

        await store.SaveAsync(new EntityVisibilityConfig());

        Assert.False((await store.LoadAsync())!.UseExposedEntitiesOnly);
    }
}