using System.Text.Json;
using lucia.Agents.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace lucia.Agents.Services;

/// <summary>
/// Redis-backed session cache for multi-turn orchestrator conversations.
/// Sessions are stored as JSON with a sliding expiration window.
/// </summary>
public sealed class RedisSessionCacheService : ISessionCacheService
{
    private const string KeyPrefix = "lucia:session:";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSessionCacheService> _logger;
    private readonly SessionCacheOptions _options;

    public RedisSessionCacheService(
        IConnectionMultiplexer redis,
        IOptions<SessionCacheOptions> options,
        ILogger<RedisSessionCacheService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SessionData?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var db = _redis.GetDatabase();
        var key = KeyPrefix + sessionId;

        try
        {
            var json = await db.StringGetAsync(key).ConfigureAwait(false);
            if (json.IsNullOrEmpty)
            {
                _logger.LogDebug("Session {SessionId} not found in cache", sessionId);
                return null;
            }

            var session = JsonSerializer.Deserialize<SessionData>(json.ToString(), SerializerOptions);
            if (session is null)
            {
                return null;
            }

            // Refresh the sliding expiration
            var ttl = TimeSpan.FromMinutes(_options.SessionCacheLengthMinutes);
            await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);

            session.LastAccessedAt = DateTime.UtcNow;
            _logger.LogDebug("Loaded session {SessionId} with {TurnCount} history turns",
                sessionId, session.History.Count);

            return session;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to load session {SessionId} from Redis", sessionId);
            return null;
        }
    }

    public async Task SaveSessionAsync(SessionData session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        // Trim history to configured max
        var maxItems = _options.MaxHistoryItems;
        if (session.History.Count > maxItems)
        {
            session.History = session.History
                .Skip(session.History.Count - maxItems)
                .ToList();
        }

        session.LastAccessedAt = DateTime.UtcNow;

        var db = _redis.GetDatabase();
        var key = KeyPrefix + session.SessionId;
        var ttl = TimeSpan.FromMinutes(_options.SessionCacheLengthMinutes);

        try
        {
            var json = JsonSerializer.Serialize(session, SerializerOptions);
            await db.StringSetAsync(key, json, ttl).ConfigureAwait(false);

            _logger.LogDebug("Saved session {SessionId} with {TurnCount} history turns (TTL: {TtlMinutes}m)",
                session.SessionId, session.History.Count, _options.SessionCacheLengthMinutes);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to save session {SessionId} to Redis", session.SessionId);
        }
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var db = _redis.GetDatabase();
        var key = KeyPrefix + sessionId;

        try
        {
            await db.KeyDeleteAsync(key).ConfigureAwait(false);
            _logger.LogDebug("Deleted session {SessionId} from cache", sessionId);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to delete session {SessionId} from Redis", sessionId);
        }
    }
}
