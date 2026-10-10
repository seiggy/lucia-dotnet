using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using StackExchange.Redis;

namespace lucia.Data.Redis;

/// <summary>
/// Stores the entity visibility configuration in Redis without an expiry. Redis errors propagate. Treating
/// an outage as "nothing saved" would reset the policy to its defaults and widen what agents can see.
/// </summary>
public sealed class RedisVisibilityConfigStore : IVisibilityConfigStore
{
    private const string VisibilityKey = "lucia:entity-visibility";

    private readonly IConnectionMultiplexer _redis;

    public RedisVisibilityConfigStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<EntityVisibilityConfig?> LoadAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync(VisibilityKey).ConfigureAwait(false);
        return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<EntityVisibilityConfig>((string)json!);
    }

    public async Task SaveAsync(EntityVisibilityConfig config, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        // No TTL — user configuration persists indefinitely
        await db.StringSetAsync(VisibilityKey, JsonSerializer.Serialize(config)).ConfigureAwait(false);
    }
}