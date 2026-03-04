namespace lucia.Agents.Models.HomeAssistant;

/// <summary>
/// Persisted configuration for entity visibility and filtering.
/// Stored in Redis at <c>lucia:entity-visibility</c> (no TTL — user configuration).
/// </summary>
public sealed class EntityVisibilityConfig
{
    /// <summary>
    /// When true, only entities exposed via HA's voice assistant configuration
    /// are loaded into the cache. Uses the <c>homeassistant/expose_entity/list</c>
    /// WebSocket command to obtain the exposed entity set.
    /// </summary>
    public bool UseExposedEntitiesOnly { get; set; }

    /// <summary>
    /// Per-entity agent visibility overrides. Key = entity_id, Value = agent names.
    /// Missing key = visible to all agents. Empty list = excluded from all agents.
    /// </summary>
    public Dictionary<string, List<string>> EntityAgentMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
