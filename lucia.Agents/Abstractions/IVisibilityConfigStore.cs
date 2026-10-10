using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Abstractions;

/// <summary>Persists which entities each agent may see, and whether only exposed entities load.</summary>
public interface IVisibilityConfigStore
{
    /// <summary>Returns the saved configuration, or <see langword="null"/> when none was saved.</summary>
    Task<EntityVisibilityConfig?> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(EntityVisibilityConfig config, CancellationToken ct = default);
}
