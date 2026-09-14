using lucia.Agents.Abstractions;

namespace lucia.Tests.Data;

internal static class PersonalMemorySearchAssertions
{
    public static async Task VerifyAsync(IMemoryStore store)
    {
        const string UserId = "personal-search-user";
        await store.StoreAsync(UserId, "favorite-drink", "tea");
        await store.StoreAsync("another-user", "favorite-drink", "coffee");
        await store.StoreAsync(UserId, "expired-preference", "tea", TimeSpan.FromSeconds(-1));
        for (var index = 0; index < 60; index++)
        {
            await store.StoreAsync(UserId, $"chat_history:{index:D3}", "tea in recent history");
        }
        foreach (var key in new[] { "CHAT_HISTORY:uppercase", "  chat_history:padded", "\t\u00a0chat_history:whitespace", "chat_history_extra" })
        {
            await store.StoreAsync(UserId, key, "tea in reserved history");
        }

        foreach (var query in new string?[] { null, "", "tea", "favorite-drink" })
        {
            var personal = await store.SearchPersonalAsync(UserId, query, limit: 1);
            var entry = Assert.Single(personal);
            Assert.Equal("favorite-drink", entry.Key);
            Assert.Equal("tea", entry.Value);
        }

        var history = await store.SearchAsync(UserId, "chat_history", limit: 1);
        Assert.Contains("chat_history", Assert.Single(history).Key, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.SearchPersonalAsync(UserId, "coffee"));
        Assert.Empty(await store.SearchPersonalAsync(UserId, limit: 0));

        await store.StoreAsync(UserId, "chatXhistory", "not the reserved prefix");
        Assert.Equal("chatXhistory", Assert.Single(await store.SearchPersonalAsync(UserId, "chatXhistory", 1)).Key);
    }
}
