using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using lucia.Data.InMemory;

namespace lucia.Tests.Services;

public sealed class ChatHistoryProviderTests
{
    [Fact]
    public async Task AppendTurnAsync_StoresLatestTurnsUnderChatHistoryPrefix()
    {
        var store = new InMemoryMemoryStore();
        var provider = new ChatHistoryProvider(store);

        await provider.AppendTurnAsync("user-1", "Hello", "Hi there");
        await provider.AppendTurnAsync("user-1", "What do I like?", "You like jazz.");

        var history = await provider.GetRecentHistoryAsync("user-1", maxTurns: 1);
        var memories = await store.SearchAsync("user-1", "chat_history:");

        Assert.Single(history);
        Assert.Contains("What do I like?", history[0]);
        Assert.Contains("chat_history:", memories[0].Key);
    }

    [Fact]
    public async Task AppendTurnAsync_StoresTurnsWithSevenDayTtl()
    {
        var store = A.Fake<IMemoryStore>();
        var provider = new ChatHistoryProvider(store);

        await provider.AppendTurnAsync("user-1", "Hello", "Hi there");

        A.CallTo(() => store.StoreAsync(
                "user-1",
                A<string>.That.StartsWith(ChatHistoryProvider.ChatHistoryKeyPrefix),
                A<string>.That.Contains("User: Hello"),
                A<TimeSpan?>.That.Matches(ttl => ttl == TimeSpan.FromDays(7)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
