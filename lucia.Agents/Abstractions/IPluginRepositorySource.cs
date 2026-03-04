using lucia.Agents.Configuration;
using lucia.Agents.PluginFramework;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Abstracts how plugin manifests are fetched and plugins are installed from a repository.
/// Implementations exist for local filesystem (dev) and remote Git hosting (production).
/// </summary>
public interface IPluginRepositorySource
{
    /// <summary>
    /// The repository type this source handles (e.g. "local", "git").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Fetches the <c>lucia-plugins.json</c> manifest from the repository.
    /// </summary>
    Task<PluginManifest?> FetchManifestAsync(PluginRepositoryDefinition repo, CancellationToken ct = default);

    /// <summary>
    /// Downloads or copies a plugin's files into the <paramref name="targetPath"/> directory.
    /// </summary>
    Task InstallPluginAsync(
        PluginRepositoryDefinition repo,
        PluginManifestEntry plugin,
        string targetPath,
        CancellationToken ct = default);
}
