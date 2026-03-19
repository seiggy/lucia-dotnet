namespace lucia.Wyoming.Models;

/// <summary>
/// No-op preference store used when MongoDB is not available.
/// </summary>
public sealed class InMemoryModelPreferenceStore : IModelPreferenceStore
{
    private readonly Dictionary<EngineType, string> _overrides = [];

    public Task<Dictionary<EngineType, string>> LoadOverridesAsync(CancellationToken ct = default)
        => Task.FromResult(new Dictionary<EngineType, string>(_overrides));

    public Task SaveOverrideAsync(EngineType engineType, string modelId, CancellationToken ct = default)
    {
        _overrides[engineType] = modelId;
        return Task.CompletedTask;
    }

    public Task RemoveOverrideAsync(EngineType engineType, CancellationToken ct = default)
    {
        _overrides.Remove(engineType);
        return Task.CompletedTask;
    }
}
