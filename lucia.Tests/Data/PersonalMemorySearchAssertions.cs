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

    public static async Task VerifyLiteralSearchSemanticsAsync(IMemoryStore store)
    {
        const string UserId = "literal-search-user";
        const string HistoryKey = "chat_history:one";
        foreach (var (key, value) in new[]
        {
            ("key%literal", "percent key"),
            ("key_literal", "underscore key"),
            ("percent-value", "100% savings"),
            ("underscore-value", "living_room"),
            ("path", @"C:\tmp\50%_ready"),
            ("quotes", "owner's \"note\""),
            ("mixed", "MiXeD CaSe"),
            ("plain", "100percent livingXroom"),
            (HistoryKey, "100% savings"),
        })
        {
            await store.StoreAsync(UserId, key, value);
            await store.StoreAsync("another-user", key, value);
        }

        (string Query, string[] Keys)[] cases =
        [
            ("%", ["key%literal", "percent-value", "path", HistoryKey]),
            ("_", ["key_literal", "underscore-value", "path", HistoryKey]),
            (@"\", ["path"]),
            (@"50%_ready", ["path"]),
            ("100%", ["percent-value", HistoryKey]),
            ("living_room", ["underscore-value"]),
            ("owner's", ["quotes"]),
            ("\"note\"", ["quotes"]),
            ("mixed case", ["mixed"]),
            ("key%literal", ["key%literal"]),
            ("key_literal", ["key_literal"]),
            ("missing", []),
        ];
        foreach (var personalOnly in new[] { false, true })
        {
            foreach (var (query, keys) in cases)
            {
                var matches = personalOnly
                    ? await store.SearchPersonalAsync(UserId, query, limit: 50)
                    : await store.SearchAsync(UserId, query, limit: 50);
                var expected = keys.Where(key => !personalOnly || key != HistoryKey);
                Assert.Equal(expected.Order(), matches.Select(entry => entry.Key).Order());
            }
        }
    }
}
