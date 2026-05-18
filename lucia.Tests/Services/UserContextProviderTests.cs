using lucia.Agents.Services;
using lucia.Data.InMemory;

namespace lucia.Tests.Services;

public sealed class UserContextProviderTests
{
    [Fact]
    public async Task GetUserContextAsync_LimitsEntriesAndTotalCharacters()
    {
        var store = new InMemoryMemoryStore();
        var provider = new UserContextProvider(store);

        for (var index = 0; index < 60; index++)
        {
            await store.StoreAsync("user-1", $"memory-{index:D2}", new string('x', 100));
        }

        var context = await provider.GetUserContextAsync("user-1", CancellationToken.None);
        var lines = context.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("USER MEMORY CONTEXT:", lines[0]);
        Assert.True(lines.Length <= 51);
        Assert.True(context.Length <= 4100);
        Assert.DoesNotContain("memory-00", context, StringComparison.Ordinal);
    }
}
