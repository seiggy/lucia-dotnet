namespace lucia.Data;

/// <summary>
/// Available cache provider types.
/// </summary>
public enum CacheProviderType
{
    /// <summary>Redis distributed cache (default, requires Redis server).</summary>
    Redis,

    /// <summary>In-process memory cache (no external dependencies).</summary>
    InMemory
}
