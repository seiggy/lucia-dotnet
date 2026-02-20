namespace lucia.AgentHost.Extensions;

/// <summary>
/// Schema for a configuration section — used by UI for dynamic form generation.
/// </summary>
public sealed class ConfigSectionSchema
{
    public string Section { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<ConfigPropertySchema> Properties { get; set; } = [];
}
