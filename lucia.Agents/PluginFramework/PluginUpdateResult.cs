namespace lucia.Agents.PluginFramework;

/// <summary>
/// Describes the outcome of a plugin update attempt.
/// </summary>
public enum PluginUpdateResult
{
    Updated,
    AlreadyUpToDate,
    PluginNotInstalled,
    PluginNotInRepository
}
