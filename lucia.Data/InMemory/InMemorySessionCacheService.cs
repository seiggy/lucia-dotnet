using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Orchestration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Data.InMemory;

/// <summary>
/// In-memory implementation of <see cref="ISessionCacheService"/> using <see cref="IMemoryCache"/>.
/// Replaces the Redis-backed version for lightweight/mono-container deployments.
/// </summary>
public sealed class InMemorySessionCacheService : ISessionCacheService
{
    private const string KeyPrefix = "session:";

    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemorySessionCacheService> _logger;
    private readonly SessionCacheOptions _options;

    public InMemorySessionCacheService(
        IMemoryCache cache,
        IOptions<SessionCacheOptions> options,
        ILogger<InMemorySessionCacheService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SessionData?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var key = KeyPrefix + sessionId;

        if (!_cache.TryGetValue(key, out SessionData? session) || session is null)
        {
            _logger.LogDebug("Session {SessionId} not found in memory cache", sessionId);
            return Task.FromResult<SessionData?>(null);
        }

        session.LastAccessedAt = DateTime.UtcNow;
        _logger.LogDebug("Loaded session {SessionId} with {TurnCount} history turns",
            sessionId, session.History.Count);

        return Task.FromResult<SessionData?>(session);
    }

    public Task SaveSessionAsync(SessionData session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        var maxItems = _options.MaxHistoryItems;
        if (session.History.Count > maxItems)
        {
            session.History = session.History
                .Skip(session.History.Count - maxItems)
                .ToList();
        }

        session.LastAccessedAt = DateTime.UtcNow;

        var key = KeyPrefix + session.SessionId;
        var entryOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(_options.SessionCacheLengthMinutes)
        };

        _cache.Set(key, session, entryOptions);

        _logger.LogDebug("Saved session {SessionId} with {TurnCount} history turns (TTL: {TtlMinutes}m)",
            session.SessionId, session.History.Count, _options.SessionCacheLengthMinutes);

        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var key = KeyPrefix + sessionId;
        _cache.Remove(key);

        _logger.LogDebug("Deleted session {SessionId} from memory cache", sessionId);
        return Task.CompletedTask;
    }
}
