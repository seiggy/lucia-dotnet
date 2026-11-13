namespace lucia.Agents.Abstractions;

/// <summary>
/// Optional capability for plugins to surface a live, read-only status line on
/// the dashboard's Plugins page (rendered under the plugin's row). Status reflects
/// what the plugin actually resolved at runtime — never user-edited. A
/// <c>null</c> status hides the line entirely.
/// </summary>
public interface IPluginStatusProvider
{
    /// <summary>Short human-readable status line, or <c>null</c> when there is nothing to report.</summary>
    string? StatusLine { get; }
}
