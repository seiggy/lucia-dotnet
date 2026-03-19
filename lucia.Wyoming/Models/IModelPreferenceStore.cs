namespace lucia.Wyoming.Models;

/// <summary>
/// Abstracts persistence of active model overrides for testability.
/// Keys are string-based to support both EngineType values and well-known preference keys.
/// </summary>
public interface IModelPreferenceStore
{
    Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default);
    Task SaveAsync(string key, string value, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}
