using lucia.Agents.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace lucia.Data.InMemory;

/// <summary>
/// In-process <see cref="IKeyValueCache"/> backed by <see cref="IMemoryCache"/>.
/// </summary>
public sealed class InMemoryKeyValueCache(IMemoryCache cache) : IKeyValueCache
{
    private const string KeyPrefix = "lucia:kv:";

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(cache.TryGetValue(KeyPrefix + key, out string? value) ? value : null);

    public Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        cache.Set(KeyPrefix + key, value, timeToLive);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cache.Remove(KeyPrefix + key);
        return Task.CompletedTask;
    }
}