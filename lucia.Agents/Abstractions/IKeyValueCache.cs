namespace lucia.Agents.Abstractions;

/// <summary>
/// Small string cache with per-entry expiry. The in-memory and Redis providers implement it,
/// so decorators that cache store reads work with whichever cache provider is configured.
/// Callers treat failures as cache misses.
/// </summary>
public interface IKeyValueCache
{
    /// <summary>Returns the cached value, or <c>null</c> when absent or expired.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value that expires after <paramref name="timeToLive"/>.</summary>
    Task SetAsync(string key, string value, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    /// <summary>Removes a value. Removing a missing key is not an error.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}