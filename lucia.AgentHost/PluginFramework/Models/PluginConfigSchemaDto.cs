namespace lucia.AgentHost.PluginFramework;

/// <summary>
/// DTO returned by the plugin config schemas endpoint.
/// </summary>
public sealed class PluginConfigSchemaDto
{
    public string PluginId { get; set; } = default!;
    public string Section { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<PluginConfigPropertyDto> Properties { get; set; } = [];
}
