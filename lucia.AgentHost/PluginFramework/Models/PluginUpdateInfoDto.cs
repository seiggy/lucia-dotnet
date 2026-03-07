namespace lucia.AgentHost.PluginFramework.Models;

/// <summary>
/// API response DTO for a plugin update notification, mapping
/// from the internal <c>PluginUpdateInfo</c> domain model.
/// </summary>
public sealed class PluginUpdateInfoDto
{
    public required string PluginId { get; set; }
    public required string PluginName { get; set; }
    public string? InstalledVersion { get; set; }
    public string? AvailableVersion { get; set; }
    public required string RepositoryId { get; set; }
}
