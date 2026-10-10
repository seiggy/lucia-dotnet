using lucia.Agents.Abstractions;
using StackExchange.Redis;

namespace lucia.Data.Redis;

/// <summary>
/// Redis-backed <see cref="IKeyValueCache"/>.
/// </summary>
public sealed class RedisKeyValueCache(IConnectionMultiplexer redis) : IKeyValueCache
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(key).ConfigureAwait(false);
        return value.HasValue ? value.ToString() : null;
    }

    public Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
        => redis.GetDatabase().StringSetAsync(key, value, timeToLive);

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => redis.GetDatabase().KeyDeleteAsync(key);
}