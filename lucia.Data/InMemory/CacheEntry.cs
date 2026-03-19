namespace lucia.Data.InMemory;

/// <summary>
/// A TTL-aware cache entry used by in-memory cache providers.
/// </summary>
internal sealed class CacheEntry<T>
{
    public T Value { get; }

    private readonly DateTimeOffset _expiresAt;

    public CacheEntry(T value, TimeSpan ttl)
    {
        Value = value;
        _expiresAt = DateTimeOffset.UtcNow + ttl;
    }

    public bool IsExpired => DateTimeOffset.UtcNow >= _expiresAt;
}
