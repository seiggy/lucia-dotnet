namespace lucia.Wyoming.Models;

/// <summary>
/// No-op preference store used when no persistent store is configured.
/// </summary>
public sealed class InMemoryModelPreferenceStore : IModelPreferenceStore
{
    private readonly Dictionary<string, string> _store = [];

    public Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default)
        => Task.FromResult(new Dictionary<string, string>(_store));

    public Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.Remove(key);
        return Task.CompletedTask;
    }
}
