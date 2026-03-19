using FakeItEasy;
using lucia.Agents.Models;
using lucia.Agents.Orchestration;
using lucia.Data.InMemory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Data;

public sealed class InMemorySessionCacheServiceTests : IDisposable
{
    private readonly MemoryCache _memoryCache;
    private readonly InMemorySessionCacheService _service;

    public InMemorySessionCacheServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new SessionCacheOptions
        {
            SessionCacheLengthMinutes = 30,
            MaxHistoryItems = 20
        });
        var logger = A.Fake<ILogger<InMemorySessionCacheService>>();
        _service = new InMemorySessionCacheService(_memoryCache, options, logger);
    }

    [Fact]
    public async Task SaveSessionAsync_And_GetSessionAsync_RoundTrips()
    {
        var session = new SessionData
        {
            SessionId = "session-1",
            History = []
        };

        await _service.SaveSessionAsync(session);

        var result = await _service.GetSessionAsync("session-1");

        Assert.NotNull(result);
        Assert.Equal("session-1", result.SessionId);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNull_ForMissingSession()
    {
        var result = await _service.GetSessionAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesSession()
    {
        var session = new SessionData
        {
            SessionId = "session-delete",
            History = []
        };
        await _service.SaveSessionAsync(session);

        await _service.DeleteSessionAsync("session-delete");

        var result = await _service.GetSessionAsync("session-delete");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveSessionAsync_UpdatesLastAccessedAt()
    {
        var session = new SessionData
        {
            SessionId = "session-access",
            LastAccessedAt = DateTime.UtcNow.AddHours(-1),
            History = []
        };

        await _service.SaveSessionAsync(session);

        var result = await _service.GetSessionAsync("session-access");
        Assert.NotNull(result);
        Assert.True(result.LastAccessedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task SaveSessionAsync_TrimsHistoryToMaxItems()
    {
        var session = new SessionData
        {
            SessionId = "session-trim",
            History = Enumerable.Range(0, 25)
                .Select(i => new SessionTurn { Role = "user", Content = $"msg-{i}" })
                .ToList()
        };

        await _service.SaveSessionAsync(session);

        var result = await _service.GetSessionAsync("session-trim");
        Assert.NotNull(result);
        Assert.Equal(20, result.History.Count);
        // Should keep the most recent items (skip older ones)
        Assert.Equal("msg-5", result.History[0].Content);
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
    }
}
