using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace lucia.Data.Redis;

/// <summary>Stores the entity visibility configuration in Redis without an expiry.</summary>
public sealed class RedisVisibilityConfigStore : IVisibilityConfigStore
{
    private const string VisibilityKey = "lucia:entity-visibility";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisVisibilityConfigStore> _logger;

    public RedisVisibilityConfigStore(IConnectionMultiplexer redis, ILogger<RedisVisibilityConfigStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<EntityVisibilityConfig?> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync(VisibilityKey).ConfigureAwait(false);
            return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<EntityVisibilityConfig>((string)json!);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to load entity visibility config from Redis, using defaults");
            return null;
        }
    }

    public async Task SaveAsync(EntityVisibilityConfig config, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            // No TTL — user configuration persists indefinitely
            await db.StringSetAsync(VisibilityKey, JsonSerializer.Serialize(config)).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to save entity visibility config to Redis");
        }
    }
}
