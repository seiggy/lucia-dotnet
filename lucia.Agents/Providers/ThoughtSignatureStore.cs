using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace lucia.Agents.Providers;

/// <summary>
/// In-memory cache of Gemini thought signatures keyed by conversation prefix and tool call.
/// Entries expire after a fixed TTL and the store evicts oldest entries once full, keeping
/// memory bounded for long-running hosts.
/// </summary>
public sealed class ThoughtSignatureStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    private const int DefaultMaxEntries = 2048;

    private readonly TimeProvider _timeProvider;
    private readonly int _maxEntries;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public ThoughtSignatureStore(TimeProvider timeProvider) : this(timeProvider, DefaultMaxEntries)
    {
    }

    public ThoughtSignatureStore(TimeProvider timeProvider, int maxEntries)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), maxEntries, "Maximum entry count must be positive.");
        }

        _maxEntries = maxEntries;
    }

    public void TryAdd(string conversationKey, string toolCallKey, string signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        if (_entries.Count >= _maxEntries)
        {
            Evict();
        }

        _entries[Combine(conversationKey, toolCallKey)] = new Entry(signature, _timeProvider.GetUtcNow());
    }

    public bool TryGet(string conversationKey, string toolCallKey, [NotNullWhen(true)] out string? signature)
    {
        var key = Combine(conversationKey, toolCallKey);
        if (_entries.TryGetValue(key, out var entry))
        {
            if (_timeProvider.GetUtcNow() - entry.AddedAt < DefaultTtl)
            {
                signature = entry.Signature;
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        signature = null;
        return false;
    }

    private void Evict()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var pair in _entries)
        {
            if (now - pair.Value.AddedAt >= DefaultTtl)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }

        if (_entries.Count >= _maxEntries)
        {
            var oldest = _entries.OrderBy(pair => pair.Value.AddedAt).First().Key;
            _entries.TryRemove(oldest, out _);
        }
    }

    private static string Combine(string conversationKey, string toolCallKey) =>
        conversationKey + ":" + toolCallKey;

    private sealed record Entry(string Signature, DateTimeOffset AddedAt);
}
