namespace lucia.AgentHost.PluginFramework;

/// <summary>
/// A single configurable property in a plugin config schema.
/// </summary>
public sealed class PluginConfigPropertyDto
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string DefaultValue { get; set; } = default!;
    public bool IsSensitive { get; set; }
}
