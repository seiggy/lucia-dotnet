namespace lucia.Agents.PluginFramework;

/// <summary>
/// Describes an available update for an installed plugin, including the
/// current installed version and the newer version available in a repository.
/// </summary>
public sealed class PluginUpdateInfo
{
    public required string PluginId { get; set; }
    public required string PluginName { get; set; }
    public string? InstalledVersion { get; set; }
    public string? AvailableVersion { get; set; }
    public required string RepositoryId { get; set; }
}
