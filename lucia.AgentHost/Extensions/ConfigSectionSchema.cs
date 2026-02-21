namespace lucia.AgentHost.Extensions;

/// <summary>
/// Schema for a configuration section — used by UI for dynamic form generation.
/// </summary>
public sealed class ConfigSectionSchema
{
    public string Section { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<ConfigPropertySchema> Properties { get; set; } = [];

    /// <summary>
    /// When true, the section represents a JSON array of items.
    /// Each item has the properties defined in <see cref="Properties"/>.
    /// Keys are stored with numeric indices (e.g., "Agents:0:AgentName").
    /// </summary>
    public bool IsArray { get; set; }
}
