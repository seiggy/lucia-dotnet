using System.Collections.Concurrent;

namespace lucia.Wyoming.WakeWord;

public sealed class InMemoryWakeWordStore : IWakeWordStore
{
    private readonly ConcurrentDictionary<string, CustomWakeWord> _store = new();

    public Task<CustomWakeWord?> GetAsync(string id, CancellationToken ct)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IReadOnlyList<CustomWakeWord>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CustomWakeWord>>(_store.Values.ToList());

    public Task SaveAsync(CustomWakeWord wakeWord, CancellationToken ct)
    {
        _store[wakeWord.Id] = wakeWord;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
