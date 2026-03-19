namespace lucia.Data.InMemory;

/// <summary>
/// A TTL-aware timestamped entry used by <see cref="InMemoryTaskStore"/>.
/// </summary>
internal sealed class TimestampedEntry<T>
{
    public T Value { get; }

    private readonly DateTimeOffset _expiresAt;

    public TimestampedEntry(T value, TimeSpan ttl)
    {
        Value = value;
        _expiresAt = DateTimeOffset.UtcNow + ttl;
    }

    public bool IsExpired => DateTimeOffset.UtcNow >= _expiresAt;
}
