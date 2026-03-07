namespace lucia.Agents.PluginFramework;

/// <summary>
/// Wraps an <see cref="InstalledPluginRecord"/> with update availability information
/// for the installed plugins API response.
/// </summary>
public sealed class InstalledPluginWithUpdateInfo
{
    public required InstalledPluginRecord Plugin { get; set; }
    public bool UpdateAvailable { get; set; }
    public string? AvailableVersion { get; set; }
}
