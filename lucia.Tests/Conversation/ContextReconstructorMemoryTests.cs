using lucia.AgentHost.Conversation;
using lucia.AgentHost.Conversation.Models;
using lucia.Agents.Services;
using lucia.Data.InMemory;

namespace lucia.Tests.Conversation;

public sealed class ContextReconstructorMemoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("enrolled-voice-profile")]
    public async Task ReconstructAsync_VoiceRequestDoesNotCopyStoredMemoriesIntoLoggedPrompt(string? profileId)
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync("enrolled-voice-profile", "favorite-drink", "private-coffee-preference");
        await store.StoreAsync("ha-service-account", "favorite-drink", "service-account-preference");
        var history = new ChatHistoryProvider(store);
        await history.AppendTurnAsync("enrolled-voice-profile", "old private request", "old private answer");
        var reconstructor = new ContextReconstructor(new UserContextProvider(store), history);
        var request = new ConversationRequest
        {
            Text = "Turn on the kitchen light.",
            Context = new ConversationContext
            {
                IsVoiceRequest = true,
                EnrolledProfileId = profileId,
                UserId = "ha-service-account",
                SpeakerId = profileId is null ? null : "Alice",
                DeviceArea = "Kitchen"
            }
        };

        var prompt = await reconstructor.ReconstructAsync(request);

        Assert.DoesNotContain("USER MEMORY CONTEXT", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("private-coffee-preference", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("service-account-preference", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("RECENT CHAT HISTORY", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("old private", prompt, StringComparison.Ordinal);
        Assert.Contains("Kitchen", prompt, StringComparison.Ordinal);
        Assert.Contains("User: Turn on the kitchen light.", prompt, StringComparison.Ordinal);
    }

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
