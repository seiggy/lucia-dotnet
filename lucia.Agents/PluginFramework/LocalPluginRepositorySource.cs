using System.Text.Json;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// Reads plugin manifests from the local filesystem and copies plugin files for installation.
/// Used in development to simulate a remote repository against the local repo checkout.
/// </summary>
public sealed class LocalPluginRepositorySource : IPluginRepositorySource
{
    private readonly ILogger<LocalPluginRepositorySource> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public LocalPluginRepositorySource(ILogger<LocalPluginRepositorySource> logger)
    {
        _logger = logger;
    }

    public string Type => "local";

    public Task<PluginManifest?> FetchManifestAsync(PluginRepositoryDefinition repo, CancellationToken ct = default)
    {
        var basePath = repo.Url; // For local repos, Url holds the local directory path
        if (string.IsNullOrWhiteSpace(basePath) || !Directory.Exists(basePath))
        {
            _logger.LogWarning("Local repository '{Id}' path '{Path}' does not exist.", repo.Id, basePath);
            return Task.FromResult<PluginManifest?>(null);
        }

        var manifestPath = Path.Combine(basePath, repo.ManifestPath);
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning(
                "Local repository '{Id}' has no manifest at '{Path}'.", repo.Id, manifestPath);
            return Task.FromResult<PluginManifest?>(null);
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions);
            _logger.LogInformation(
                "Fetched local manifest for '{Id}' — {Count} plugins.",
                repo.Id, manifest?.Plugins.Count ?? 0);
            return Task.FromResult(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read local manifest for repository '{Id}'.", repo.Id);
            return Task.FromResult<PluginManifest?>(null);
        }
    }

    public Task InstallPluginAsync(
        PluginRepositoryDefinition repo,
        PluginManifestEntry plugin,
        string targetPath,
        CancellationToken ct = default)
    {
        var basePath = repo.Url;
        if (string.IsNullOrWhiteSpace(basePath))
            throw new InvalidOperationException($"Local repository '{repo.Id}' has no path configured.");

        var sourcePath = Path.Combine(basePath, plugin.Path);
        if (!Directory.Exists(sourcePath))
            throw new InvalidOperationException($"Plugin source folder '{sourcePath}' does not exist.");

        _logger.LogInformation("Installing plugin '{Id}' from local path '{Path}'.", plugin.Id, sourcePath);

        if (Directory.Exists(targetPath))
            Directory.Delete(targetPath, recursive: true);

        Directory.CreateDirectory(targetPath);

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = System.IO.Path.GetRelativePath(sourcePath, file);
            var destPath = Path.Combine(targetPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, overwrite: true);
        }

        if (!File.Exists(Path.Combine(targetPath, "plugin.cs")))
        {
            _logger.LogError("Local plugin '{Id}' has no plugin.cs — removing.", plugin.Id);
            Directory.Delete(targetPath, recursive: true);
            throw new InvalidOperationException(
                $"Local plugin '{plugin.Id}' does not contain a plugin.cs entry point.");
        }

        return Task.CompletedTask;
    }
}
