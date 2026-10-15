using lucia.Wyoming.Models;

namespace lucia.Tests.Data.Providers;

public abstract class ModelPreferenceStoreContract
{
    protected abstract Task<StoreScope<IModelPreferenceStore>> CreateAsync();

    [Fact]
    public async Task LoadAllAsync_EmptyStore_ReturnsEmpty()
    {
        await using var scope = await CreateAsync();

        Assert.Empty(await scope.Store.LoadAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_PersistsAndUpdatesTheValueForTheSameKey()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SaveAsync("assistant:tts", "claude-3.5-sonnet", CancellationToken.None);

        var loaded = await scope.Store.LoadAllAsync(CancellationToken.None);
        Assert.Equal("claude-3.5-sonnet", loaded["assistant:tts"]);

        await scope.Store.SaveAsync("assistant:tts", "gpt-4o-mini", CancellationToken.None);

        loaded = await scope.Store.LoadAllAsync(CancellationToken.None);
        Assert.Equal("gpt-4o-mini", loaded["assistant:tts"]);
        Assert.Single(loaded);
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyTheTargetKey()
    {
        await using var scope = await CreateAsync();

        await scope.Store.SaveAsync("assistant:tts", "claude-3.5-sonnet", CancellationToken.None);
        await scope.Store.SaveAsync("assistant:stt", "whisper-large-v3", CancellationToken.None);

        await scope.Store.RemoveAsync("assistant:tts", CancellationToken.None);

        var loaded = await scope.Store.LoadAllAsync(CancellationToken.None);
        var single = Assert.Single(loaded);
        Assert.Equal("assistant:stt", single.Key);
        Assert.Equal("whisper-large-v3", single.Value);
    }
}
