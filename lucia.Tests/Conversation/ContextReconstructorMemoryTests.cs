using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Services;
using lucia.Data.InMemory;

namespace lucia.Tests.Conversation;

public sealed class ContextReconstructorMemoryTests
{
    [Fact]
    public async Task ReconstructAsync_IncludesUserContextAndRecentHistory()
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync("user-1", "favorite-drink", "coffee");

        var historyProvider = new ChatHistoryProvider(store);
        await historyProvider.AppendTurnAsync("user-1", "Remember my drink", "I will remember coffee.");

        var reconstructor = new ContextReconstructor(new UserContextProvider(store), historyProvider);
        var request = new ConversationRequest
        {
            Text = "What do you know about me?",
            Context = new ConversationContext
            {
                Timestamp = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero),
                UserId = "user-1",
                DeviceId = "device-1"
            }
        };

        var prompt = await reconstructor.ReconstructAsync(request, CancellationToken.None);

        Assert.Contains("USER MEMORY CONTEXT", prompt);
        Assert.Contains("favorite-drink: coffee", prompt);
        Assert.Contains("RECENT CHAT HISTORY", prompt);
        Assert.Contains("Remember my drink", prompt);
        Assert.Contains("User: What do you know about me?", prompt);
    }
}
