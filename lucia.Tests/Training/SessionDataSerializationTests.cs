using System.Text.Json;
using lucia.Agents.Services;

namespace lucia.Tests.Training;

public sealed class SessionDataSerializationTests
{
    [Fact]
    public void SessionData_RoundTrips_ThroughJson()
    {
        var session = new SessionData
        {
            SessionId = "test-session-1",
            History =
            [
                new SessionTurn { Role = "user", Content = "Turn on the kitchen lights" },
                new SessionTurn { Role = "assistant", Content = "Done! Kitchen lights are now on." },
                new SessionTurn { Role = "user", Content = "Now dim them to 50%" }
            ]
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var json = JsonSerializer.Serialize(session, options);
        var deserialized = JsonSerializer.Deserialize<SessionData>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("test-session-1", deserialized.SessionId);
        Assert.Equal(3, deserialized.History.Count);
        Assert.Equal("user", deserialized.History[0].Role);
        Assert.Equal("Turn on the kitchen lights", deserialized.History[0].Content);
        Assert.Equal("assistant", deserialized.History[1].Role);
        Assert.Equal("Now dim them to 50%", deserialized.History[2].Content);
    }

    [Fact]
    public void SessionData_EmptyHistory_SerializesCorrectly()
    {
        var session = new SessionData { SessionId = "empty-session" };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        var json = JsonSerializer.Serialize(session, options);
        var deserialized = JsonSerializer.Deserialize<SessionData>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("empty-session", deserialized.SessionId);
        Assert.Empty(deserialized.History);
    }
}
