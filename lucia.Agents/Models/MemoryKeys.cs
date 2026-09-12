namespace lucia.Agents.Models;

public static class MemoryKeys
{
    public const string ChatHistoryPrefix = "chat_history";

    // The .NET TrimStart whitespace set, shared with datastore prefix filters.
    public const string LeadingWhitespace = "\u0009\u000a\u000b\u000c\u000d\u0020\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000";

    public static bool IsChatHistory(string key) =>
        key.TrimStart().StartsWith(ChatHistoryPrefix, StringComparison.OrdinalIgnoreCase);
}
