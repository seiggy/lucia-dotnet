using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;

namespace lucia.Agents.Services;

/// <summary>
/// Keeps the entity visibility configuration in the SQLite or PostgreSQL configuration store, so it
/// survives restarts when Redis is not in use.
/// </summary>
public sealed class ConfigStoreVisibilityConfigStore : IVisibilityConfigStore
{
    /// <summary>Config section that holds the entry. The dashboard hides it from the settings list.</summary>
    public const string Section = "EntityVisibility";

    private const string Key = Section + ":Config";

    private readonly IConfigStoreWriter _configStore;

    public ConfigStoreVisibilityConfigStore(IConfigStoreWriter configStore)
    {
        _configStore = configStore;
    }

    public async Task<EntityVisibilityConfig?> LoadAsync(CancellationToken ct = default)
    {
        var json = await _configStore.GetAsync(Key, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<EntityVisibilityConfig>(json);
    }

    public Task SaveAsync(EntityVisibilityConfig config, CancellationToken ct = default)
        => _configStore.SetAsync(Key, JsonSerializer.Serialize(config), updatedBy: "entity-visibility", cancellationToken: ct);
}