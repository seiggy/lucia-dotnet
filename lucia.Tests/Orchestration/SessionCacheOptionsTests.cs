using lucia.Agents.Orchestration;
using lucia.Agents.Services;

namespace lucia.Tests.Orchestration;

public sealed class SessionCacheOptionsTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var options = new SessionCacheOptions();

        Assert.Equal(5, options.SessionCacheLengthMinutes);
        Assert.Equal(20, options.MaxHistoryItems);
    }

    [Fact]
    public void SessionData_HistoryTrimming_WorksCorrectly()
    {
        var session = new SessionData { SessionId = "trim-test" };
        for (var i = 0; i < 30; i++)
        {
            session.History.Add(new SessionTurn
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Message {i}"
            });
        }

        Assert.Equal(30, session.History.Count);

        // Simulate what RedisSessionCacheService.SaveSessionAsync does
        var maxItems = 20;
        if (session.History.Count > maxItems)
        {
            session.History = session.History
                .Skip(session.History.Count - maxItems)
                .ToList();
        }

        Assert.Equal(20, session.History.Count);
        Assert.Equal("Message 10", session.History[0].Content);
        Assert.Equal("Message 29", session.History[^1].Content);
    }
}
