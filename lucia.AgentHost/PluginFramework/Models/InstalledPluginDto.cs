namespace lucia.AgentHost.PluginFramework.Models;

/// <summary>
/// API response DTO for an installed plugin with update availability information.
/// </summary>
public sealed class InstalledPluginDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Version { get; set; }
    public required string Source { get; set; }
    public string? RepositoryId { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }

    /// <summary>
    /// Live status line reported by the plugin via IPluginStatusProvider, or null when
    /// the plugin does not implement it (or has nothing to report). Read-only, for display.
    /// </summary>
    public string? Status { get; set; }
    public required string PluginPath { get; set; }
    public bool Enabled { get; set; }
    public DateTime InstalledAt { get; set; }
    public bool UpdateAvailable { get; set; }
    public string? AvailableVersion { get; set; }
}
