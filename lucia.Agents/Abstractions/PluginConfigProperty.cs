namespace lucia.Agents.Abstractions;

/// <summary>
/// Describes a single configurable property exposed by a plugin.
/// Used by the dashboard UI to generate dynamic configuration forms.
/// </summary>
public sealed record PluginConfigProperty(
    string Name,
    string Type,
    string Description,
    string DefaultValue,
    bool IsSensitive = false);
