namespace lucia.Wyoming.Models;

/// <summary>
/// Abstracts persistence of active model overrides for testability.
/// </summary>
public interface IModelPreferenceStore
{
    Task<Dictionary<EngineType, string>> LoadOverridesAsync(CancellationToken ct = default);
    Task SaveOverrideAsync(EngineType engineType, string modelId, CancellationToken ct = default);
    Task RemoveOverrideAsync(EngineType engineType, CancellationToken ct = default);
}
