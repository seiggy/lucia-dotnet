using System.IO.Compression;
using System.Text.Json;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// Fetches plugin manifests from remote Git repositories (e.g. GitHub) and downloads
/// plugin archives for installation. Supports three blob source strategies:
/// <list type="bullet">
///   <item><c>release</c> — prefer GitHub Release assets, fall back to release zipball</item>
///   <item><c>tag</c> — download archive at a specific tag</item>
///   <item><c>branch</c> — download archive at branch HEAD</item>
/// </list>
/// </summary>
public sealed class GitPluginRepositorySource : IPluginRepositorySource
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitPluginRepositorySource> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public GitPluginRepositorySource(
        IHttpClientFactory httpClientFactory,
        ILogger<GitPluginRepositorySource> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Type => "git";

    public async Task<PluginManifest?> FetchManifestAsync(
        PluginRepositoryDefinition repo, CancellationToken ct = default)
    {
        var rawUrl = BuildRawContentUrl(repo, repo.ManifestPath);
        _logger.LogDebug("Fetching manifest from {Url}.", rawUrl);

        try
        {
            using var client = _httpClientFactory.CreateClient("PluginRepos");
            var json = await client.GetStringAsync(rawUrl, ct).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions);

            _logger.LogInformation(
                "Fetched manifest for '{Id}' — {Count} plugins.",
                repo.Id, manifest?.Plugins.Count ?? 0);
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch manifest for repository '{Id}' from {Url}.", repo.Id, rawUrl);
            throw;
        }
    }

    public Task InstallPluginAsync(
        PluginRepositoryDefinition repo,
        PluginManifestEntry plugin,
        string targetPath,
        CancellationToken ct = default)
    {
        var strategy = repo.BlobSource?.ToLowerInvariant() ?? "release";
        return strategy switch
        {
            "release" => InstallFromReleaseAsync(repo, plugin, targetPath, ct),
            "tag" => InstallFromArchiveAsync(repo, plugin, targetPath, isTag: true, ct),
            "branch" => InstallFromArchiveAsync(repo, plugin, targetPath, isTag: false, ct),
            _ => throw new InvalidOperationException(
                $"Unknown blobSource '{repo.BlobSource}' for repository '{repo.Id}'. " +
                "Expected 'release', 'tag', or 'branch'."),
        };
    }

    // ── Release Strategy ────────────────────────────────────────────

    /// <summary>
    /// Attempts to install from a GitHub Release:
    /// 1. Fetch the latest release via GitHub REST API
    /// 2. Look for a release asset named <c>{pluginId}.zip</c>
    /// 3. If found, download and extract that asset (pre-built plugin zip)
    /// 4. If not found, fall back to the release's <c>zipball_url</c> and extract the plugin subfolder
    /// </summary>
    private async Task InstallFromReleaseAsync(
        PluginRepositoryDefinition repo,
        PluginManifestEntry plugin,
        string targetPath,
        CancellationToken ct)
    {
        var (owner, repoName) = ParseGitHubOwnerRepo(repo.Url!);
        var releaseUrl = $"https://api.github.com/repos/{owner}/{repoName}/releases/latest";

        _logger.LogDebug("Fetching latest release from {Url}.", releaseUrl);

        using var client = CreateGitHubApiClient();

        HttpResponseMessage releaseResponse;
        try
        {
            releaseResponse = await client.GetAsync(releaseUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch releases for '{RepoId}' — falling back to branch archive.", repo.Id);
            await InstallFromArchiveAsync(repo, plugin, targetPath, isTag: false, ct).ConfigureAwait(false);
            return;
        }

        if (!releaseResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "No releases found for '{RepoId}' (HTTP {Status}) — falling back to branch archive.",
                repo.Id, (int)releaseResponse.StatusCode);
            await InstallFromArchiveAsync(repo, plugin, targetPath, isTag: false, ct).ConfigureAwait(false);
            return;
        }

        var releaseJson = await releaseResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson, JsonOptions);

        if (release is null)
        {
            _logger.LogWarning("Could not parse release JSON for '{RepoId}' — falling back to branch.", repo.Id);
            await InstallFromArchiveAsync(repo, plugin, targetPath, isTag: false, ct).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation(
            "Latest release for '{RepoId}': {Tag} ({Name}), {AssetCount} assets.",
            repo.Id, release.TagName, release.Name, release.Assets?.Count ?? 0);

        // Strategy: look for a per-plugin asset named "{pluginId}.zip"
        var pluginAsset = release.Assets?
            .FirstOrDefault(a => a.Name.Equals($"{plugin.Id}.zip", StringComparison.OrdinalIgnoreCase));

        if (pluginAsset is not null)
        {
            _logger.LogInformation(
                "Found release asset '{AssetName}' for plugin '{PluginId}'.",
                pluginAsset.Name, plugin.Id);
            await DownloadAndExtractAssetAsync(pluginAsset.BrowserDownloadUrl, targetPath, ct)
                .ConfigureAwait(false);
            return;
        }

        // Fallback: use the release zipball (full repo at that tag), extract plugin subfolder
        if (!string.IsNullOrEmpty(release.ZipballUrl))
        {
            _logger.LogInformation(
                "No per-plugin asset found — downloading release zipball for '{PluginId}'.", plugin.Id);
            await DownloadAndExtractSubfolderAsync(release.ZipballUrl, plugin, targetPath, ct)
                .ConfigureAwait(false);
            return;
        }

        // Last resort: branch archive
        _logger.LogWarning("Release has no zipball URL — falling back to branch archive.");
        await InstallFromArchiveAsync(repo, plugin, targetPath, isTag: false, ct).ConfigureAwait(false);
    }

    // ── Archive Strategy (tag or branch) ────────────────────────────

    private async Task InstallFromArchiveAsync(
        PluginRepositoryDefinition repo,
        PluginManifestEntry plugin,
        string targetPath,
        bool isTag,
        CancellationToken ct)
    {
        var archiveUrl = BuildArchiveUrl(repo, isTag);
        _logger.LogInformation(
            "Installing plugin '{PluginId}' from {Strategy} archive: {Url}.",
            plugin.Id, isTag ? "tag" : "branch", archiveUrl);

        await DownloadAndExtractSubfolderAsync(archiveUrl, plugin, targetPath, ct).ConfigureAwait(false);
    }

    // ── Download Helpers ────────────────────────────────────────────

    /// <summary>
    /// Downloads a pre-built plugin zip asset and extracts all contents to <paramref name="targetPath"/>.
    /// The zip is expected to contain plugin files directly (no top-level wrapper directory).
    /// </summary>
    private async Task DownloadAndExtractAssetAsync(
        string downloadUrl, string targetPath, CancellationToken ct)
    {
        using var client = CreateGitHubApiClient();
        using var response = await client.GetAsync(downloadUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var zipStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        if (Directory.Exists(targetPath))
        {
            try
            {
                Directory.Delete(targetPath, recursive: true);
            }
            catch (IOException ex) when (File.Exists(Path.Combine(targetPath, "plugin.cs")))
            {
                _logger.LogInformation(
                    ex,
                    "Plugin directory '{Path}' could not be removed (e.g. read-only in container); existing plugin.cs present — registering as installed without re-extract.",
                    targetPath);
                return;
            }
            catch (UnauthorizedAccessException ex) when (File.Exists(Path.Combine(targetPath, "plugin.cs")))
            {
                _logger.LogInformation(
                    ex,
                    "Plugin directory '{Path}' could not be removed (e.g. read-only in container); existing plugin.cs present — registering as installed without re-extract.",
                    targetPath);
                return;
            }
        }
        Directory.CreateDirectory(targetPath);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                continue;

            var destPath = Path.GetFullPath(Path.Combine(targetPath, entry.FullName));
            if (!destPath.StartsWith(Path.GetFullPath(targetPath) + Path.DirectorySeparatorChar))
            {
                _logger.LogWarning("Skipping zip entry with path traversal: {Entry}", entry.FullName);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            await using var entryStream = entry.Open();
            await using var fileStream = File.Create(destPath);
            await entryStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
        }

        if (!File.Exists(Path.Combine(targetPath, "plugin.cs")))
        {
            _logger.LogError("Downloaded asset has no plugin.cs — removing.");
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, recursive: true);
            throw new InvalidOperationException("Plugin asset does not contain a plugin.cs entry point.");
        }
    }

    /// <summary>
    /// Downloads a full repo zipball and extracts only the plugin subfolder to <paramref name="targetPath"/>.
    /// GitHub zipballs have a top-level directory like "owner-repo-sha/".
    /// </summary>
    private async Task DownloadAndExtractSubfolderAsync(
        string downloadUrl,
        PluginManifestEntry plugin,
        string targetPath,
        CancellationToken ct)
    {
        using var client = CreateGitHubApiClient();
        using var response = await client.GetAsync(downloadUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var zipStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        if (Directory.Exists(targetPath))
        {
            try
            {
                Directory.Delete(targetPath, recursive: true);
            }
            catch (IOException ex) when (File.Exists(Path.Combine(targetPath, "plugin.cs")))
            {
                _logger.LogInformation(
                    ex,
                    "Plugin directory '{Path}' could not be removed (e.g. read-only in container); existing plugin.cs present — registering as installed without re-extract.",
                    targetPath);
                return;
            }
            catch (UnauthorizedAccessException ex) when (File.Exists(Path.Combine(targetPath, "plugin.cs")))
            {
                _logger.LogInformation(
                    ex,
                    "Plugin directory '{Path}' could not be removed (e.g. read-only in container); existing plugin.cs present — registering as installed without re-extract.",
                    targetPath);
                return;
            }
        }
        Directory.CreateDirectory(targetPath);

        var pluginPathNormalized = plugin.Path.Replace('\\', '/').TrimEnd('/') + "/";
        var extracted = false;

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                continue;

            // Strip the top-level directory from the zip
            var slashIndex = entry.FullName.IndexOf('/');
            if (slashIndex < 0)
                continue;

            var relativePath = entry.FullName[(slashIndex + 1)..];

            if (!relativePath.StartsWith(pluginPathNormalized, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileRelative = relativePath[pluginPathNormalized.Length..];
            if (string.IsNullOrEmpty(fileRelative))
                continue;

            var destPath = Path.GetFullPath(Path.Combine(targetPath, fileRelative));
            if (!destPath.StartsWith(Path.GetFullPath(targetPath) + Path.DirectorySeparatorChar))
            {
                _logger.LogWarning("Skipping zip entry with path traversal: {Entry}", entry.FullName);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            await using var entryStream = entry.Open();
            await using var fileStream = File.Create(destPath);
            await entryStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            extracted = true;
        }

        if (!extracted || !File.Exists(Path.Combine(targetPath, "plugin.cs")))
        {
            _logger.LogError("Downloaded archive for '{PluginId}' has no plugin.cs — removing.", plugin.Id);
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, recursive: true);
            throw new InvalidOperationException(
                $"Plugin '{plugin.Id}' archive does not contain a plugin.cs entry point.");
        }
    }

    // ── URL Builders ────────────────────────────────────────────────

    private static string BuildRawContentUrl(PluginRepositoryDefinition repo, string filePath)
    {
        var url = repo.Url!.TrimEnd('/');
        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            url = url.Replace("github.com", "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
            return $"{url}/{repo.Branch}/{filePath}";
        }

        return $"{url}/raw/{repo.Branch}/{filePath}";
    }

    private static string BuildArchiveUrl(PluginRepositoryDefinition repo, bool isTag)
    {
        var url = repo.Url!.TrimEnd('/');
        var refPrefix = isTag ? "refs/tags" : "refs/heads";

        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            return $"{url}/archive/{refPrefix}/{repo.Branch}.zip";

        return $"{url}/archive/{repo.Branch}.zip";
    }

    /// <summary>
    /// Extracts "owner" and "repo" from a GitHub URL like "https://github.com/owner/repo".
    /// </summary>
    internal static (string Owner, string Repo) ParseGitHubOwnerRepo(string url)
    {
        var uri = new Uri(url.TrimEnd('/'));
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2)
            throw new InvalidOperationException($"Cannot parse owner/repo from URL: {url}");

        return (segments[0], segments[1]);
    }

    private HttpClient CreateGitHubApiClient()
    {
        var client = _httpClientFactory.CreateClient("PluginRepos");
        // GitHub API requires a User-Agent header
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "lucia-plugin-system");
        // Accept header for GitHub API JSON responses
        if (!client.DefaultRequestHeaders.Contains("Accept"))
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return client;
    }
}
