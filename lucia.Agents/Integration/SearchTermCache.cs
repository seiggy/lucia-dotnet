using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// Thread-safe bounded cache for search term pre-computation results.
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for lock-free reads
/// and approximate-LRU eviction based on last-access timestamps when capacity
/// is exceeded. Cache hits update the access timestamp so frequently used
/// terms are retained.
/// </summary>
public sealed class SearchTermCache
{
    private readonly int _capacity;
    private readonly int _evictCount;
    private readonly ConcurrentDictionary<string, CacheEntry> _map;

    public SearchTermCache(int capacity = 200)
    {
        _capacity = capacity;
        _evictCount = Math.Max(1, capacity / 10); // evict 10% at a time
        _map = new ConcurrentDictionary<string, CacheEntry>(
            concurrencyLevel: Environment.ProcessorCount,
            capacity: capacity,
            comparer: StringComparer.Ordinal);
    }

    /// <summary>
    /// Attempts to retrieve a cached entry for the given normalized search term.
    /// On hit, updates the last-access timestamp to keep the entry alive.
    /// Lock-free on the read path.
    /// </summary>
    public bool TryGet(string normalizedKey, out CachedSearchTerm result)
    {
        if (_map.TryGetValue(normalizedKey, out var entry))
        {
            entry.TouchTicks = Environment.TickCount64;
            result = entry.Value;
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Stores a pre-computed search term result in the cache.
    /// If capacity is exceeded, evicts the least-recently-accessed entries.
    /// </summary>
    public void Put(string normalizedKey, CachedSearchTerm value)
    {
        var entry = new CacheEntry(value) { TouchTicks = Environment.TickCount64 };
        _map[normalizedKey] = entry;

        if (_map.Count > _capacity)
            EvictOldest();
    }

    /// <summary>
    /// Removes all entries from the cache.
    /// Call when the underlying entity data is refreshed.
    /// </summary>
    public void Clear() => _map.Clear();

    public int Count => _map.Count;

    private void EvictOldest()
    {
        // Snapshot keys + timestamps, sort by oldest, remove the bottom N
        var entries = _map.ToArray();
        if (entries.Length <= _capacity)
            return;

        Array.Sort(entries, (a, b) => a.Value.TouchTicks.CompareTo(b.Value.TouchTicks));

        var toRemove = entries.Length - _capacity + _evictCount;
        for (var i = 0; i < toRemove && i < entries.Length; i++)
            _map.TryRemove(entries[i].Key, out _);
    }

    private sealed class CacheEntry(CachedSearchTerm value)
    {
        public CachedSearchTerm Value { get; } = value;

        // Intentionally non-volatile: approximate LRU is fine —
        // a stale read just means slightly less accurate eviction ordering.
        public long TouchTicks { get; set; }
    }
}

/// <summary>
/// Pre-computed search data for a normalized search term.
/// </summary>
public readonly record struct CachedSearchTerm(
    Embedding<float> Embedding,
    string[] PhoneticKeys,
    string NormalizedText);
