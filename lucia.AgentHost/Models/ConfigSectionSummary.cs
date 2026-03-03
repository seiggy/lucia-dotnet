namespace lucia.AgentHost.Models;

/// <summary>
/// Summary of a configuration section.
/// </summary>
public sealed class ConfigSectionSummary
{
    public string Section { get; set; } = default!;
    public int KeyCount { get; set; }
    public DateTime LastUpdated { get; set; }
}
