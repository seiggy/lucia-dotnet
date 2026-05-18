using System.Collections.Concurrent;
using System.Linq;

using lucia.Agents.Abstractions;
using lucia.Agents.Models;

namespace lucia.Data.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IMemoryStore"/>.
/// </summary>
public sealed class InMemoryMemoryStore : IMemoryStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MemoryEntry>> _userMemories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task StoreAsync(string userId, string key, string value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var createdAt = DateTime.UtcNow;
        var expiresAt = ttl.HasValue ? createdAt.Add(ttl.Value) : (DateTime?)null;
        var userStore = _userMemories.GetOrAdd(userId, static _ => new ConcurrentDictionary<string, MemoryEntry>(StringComparer.OrdinalIgnoreCase));
        userStore[key] = new MemoryEntry(key, value, createdAt, expiresAt);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string?> RetrieveAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_userMemories.TryGetValue(userId, out var userStore))
        {
            return Task.FromResult<string?>(null);
        }

        if (!userStore.TryGetValue(key, out var entry))
        {
            return Task.FromResult<string?>(null);
        }

        if (IsExpired(entry, DateTime.UtcNow))
        {
            userStore.TryRemove(key, out _);
            RemoveEmptyUserStore(userId, userStore);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(entry.Value);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MemoryEntry>> SearchAsync(string userId, string? query = null, int limit = 20, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (limit <= 0 || !_userMemories.TryGetValue(userId, out var userStore))
        {
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);
        }

        var now = DateTime.UtcNow;
        var entries = GetActiveEntries(userId, userStore, now);
        if (!string.IsNullOrWhiteSpace(query))
        {
            entries = entries
                .Where(entry => entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || entry.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        IReadOnlyList<MemoryEntry> results = entries
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult(results);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string userId, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_userMemories.TryGetValue(userId, out var userStore))
        {
            userStore.TryRemove(key, out _);
            RemoveEmptyUserStore(userId, userStore);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MemoryEntry>> GetAllAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_userMemories.TryGetValue(userId, out var userStore))
        {
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([]);
        }

        IReadOnlyList<MemoryEntry> results = GetActiveEntries(userId, userStore, DateTime.UtcNow)
            .OrderByDescending(entry => entry.CreatedAt)
            .ToList();

        return Task.FromResult(results);
    }

    private static bool IsExpired(MemoryEntry entry, DateTime now)
    {
        return entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= now;
    }

    private List<MemoryEntry> GetActiveEntries(string userId, ConcurrentDictionary<string, MemoryEntry> userStore, DateTime now)
    {
        var activeEntries = new List<MemoryEntry>();

        foreach (var pair in userStore)
        {
            if (IsExpired(pair.Value, now))
            {
                userStore.TryRemove(pair.Key, out _);
                continue;
            }

            activeEntries.Add(pair.Value);
        }

        RemoveEmptyUserStore(userId, userStore);
        return activeEntries;
    }

    private void RemoveEmptyUserStore(string userId, ConcurrentDictionary<string, MemoryEntry> userStore)
    {
        if (userStore.IsEmpty)
        {
            _userMemories.TryRemove(userId, out _);
        }
    }
}
