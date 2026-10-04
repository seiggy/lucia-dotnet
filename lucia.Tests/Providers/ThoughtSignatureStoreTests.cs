using lucia.Agents.Providers;
using lucia.Tests.TestDoubles;

namespace lucia.Tests.Providers;

public sealed class ThoughtSignatureStoreTests
{
    [Fact]
    public void StoresAndRetrievesSignatureForConversationAndToolCall()
    {
        var store = new ThoughtSignatureStore(TimeProvider.System);

        store.TryAdd("conv-1", "id:call_1", "SIG-A");

        Assert.True(store.TryGet("conv-1", "id:call_1", out var signature));
        Assert.Equal("SIG-A", signature);
    }

    [Fact]
    public void DoesNotLeakSignaturesAcrossConversations()
    {
        var store = new ThoughtSignatureStore(TimeProvider.System);

        store.TryAdd("conv-1", "id:call_1", "SIG-A");

        Assert.False(store.TryGet("conv-2", "id:call_1", out _));
    }

    [Fact]
    public void DoesNotLeakSignaturesAcrossToolCalls()
    {
        var store = new ThoughtSignatureStore(TimeProvider.System);

        store.TryAdd("conv-1", "id:call_1", "SIG-A");

        Assert.False(store.TryGet("conv-1", "id:call_2", out _));
    }

    [Fact]
    public void RetainsEntriesWithinTtl()
    {
        var time = new AdvancingTimeProvider();
        var store = new ThoughtSignatureStore(time);

        store.TryAdd("conv-1", "id:call_1", "SIG-A");
        time.Advance(TimeSpan.FromMinutes(29));

        Assert.True(store.TryGet("conv-1", "id:call_1", out _));
    }

    [Fact]
    public void ExpiresEntriesAfterTtl()
    {
        var time = new AdvancingTimeProvider();
        var store = new ThoughtSignatureStore(time);

        store.TryAdd("conv-1", "id:call_1", "SIG-A");
        time.Advance(TimeSpan.FromMinutes(31));

        Assert.False(store.TryGet("conv-1", "id:call_1", out _));
    }

    [Fact]
    public void OverwritesExistingEntryForSameKey()
    {
        var store = new ThoughtSignatureStore(TimeProvider.System);

        store.TryAdd("conv-1", "id:call_1", "SIG-A");
        store.TryAdd("conv-1", "id:call_1", "SIG-B");

        Assert.True(store.TryGet("conv-1", "id:call_1", out var signature));
        Assert.Equal("SIG-B", signature);
    }

    [Fact]
    public void EvictsOldestEntryWhenAtCapacity()
    {
        var time = new AdvancingTimeProvider();
        var store = new ThoughtSignatureStore(time, maxEntries: 2);

        store.TryAdd("conv-1", "id:a", "SIG-A");
        time.Advance(TimeSpan.FromMinutes(1));
        store.TryAdd("conv-1", "id:b", "SIG-B");
        time.Advance(TimeSpan.FromMinutes(1));
        store.TryAdd("conv-2", "id:c", "SIG-C");

        Assert.False(store.TryGet("conv-1", "id:a", out _));
        Assert.True(store.TryGet("conv-1", "id:b", out _));
        Assert.True(store.TryGet("conv-2", "id:c", out _));
    }
}
