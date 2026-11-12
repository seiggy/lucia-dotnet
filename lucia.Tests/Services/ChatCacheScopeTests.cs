using lucia.Agents.Services;

namespace lucia.Tests.Services;

public sealed class ChatCacheScopeTests
{
    [Fact]
    public void Matches_ReturnsTrue_WhenHashesAreEqual()
    {
        Assert.True(ChatCacheScope.Matches("abc123", "abc123"));
    }

    [Fact]
    public void Matches_ReturnsFalse_WhenHashesDiffer()
    {
        Assert.False(ChatCacheScope.Matches("agent-a-hash", "agent-b-hash"));
    }

    [Fact]
    public void Matches_ReturnsFalse_WhenStoredHashIsMissing()
    {
        // Legacy entries stored before InstructionsHash existed must never be served semantically.
        Assert.False(ChatCacheScope.Matches(null, "abc123"));
        Assert.False(ChatCacheScope.Matches(string.Empty, "abc123"));
    }

    [Fact]
    public void Matches_ReturnsFalse_WhenExpectedHashIsMissing()
    {
        // Callers without system instructions (e.g. the router) must never get semantic hits.
        Assert.False(ChatCacheScope.Matches("abc123", null));
        Assert.False(ChatCacheScope.Matches("abc123", string.Empty));
    }
}
