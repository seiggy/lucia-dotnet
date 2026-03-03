namespace lucia.Agents.PluginFramework;

/// <summary>
/// Singleton that tracks whether plugin changes (install, uninstall, enable, disable)
/// have occurred since the last host startup. The dashboard polls this to show a
/// restart banner when changes require a host restart to take effect.
/// </summary>
public sealed class PluginChangeTracker
{
    private volatile bool _restartRequired;

    /// <summary>
    /// Whether a restart is required for plugin changes to take effect.
    /// </summary>
    public bool IsRestartRequired => _restartRequired;

    /// <summary>
    /// Signals that a plugin change has occurred and a restart is required.
    /// </summary>
    public void MarkRestartRequired() => _restartRequired = true;

    /// <summary>
    /// Clears the restart flag. Called during host startup after plugins are loaded.
    /// </summary>
    public void ClearRestartRequired() => _restartRequired = false;
}
