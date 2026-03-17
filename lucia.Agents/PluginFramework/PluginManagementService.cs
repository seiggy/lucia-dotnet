using System.Collections.Concurrent;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// Orchestrates plugin management operations: syncing repository manifests,
/// installing plugins, and managing enable/disable state.
/// Delegates to <see cref="IPluginRepositorySource"/> implementations for
/// manifest fetching and plugin downloading.
/// </summary>
public sealed class PluginManagementService
{
    private readonly IPluginManagementRepository _repository;
    private readonly PluginChangeTracker _changeTracker;
    private readonly Dictionary<string, IPluginRepositorySource> _sources;
    private readonly ILogger<PluginManagementService> _logger;
    private readonly string _pluginDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _updateLocks = new(StringComparer.OrdinalIgnoreCase);

    public PluginManagementService(
        IPluginManagementRepository repository,
        PluginChangeTracker changeTracker,
        IEnumerable<IPluginRepositorySource> sources,
        ILogger<PluginManagementService> logger,
        string pluginDirectory)
    {
        _repository = repository;
        _changeTracker = changeTracker;
        _sources = sources.ToDictionary(s => s.Type, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _pluginDirectory = pluginDirectory;
    }

    // ── Bootstrap ────────────────────────────────────────────────

    /// <summary>
    /// Ensures a repository definition exists in MongoDB. Call at startup to
    /// seed the default repository (local for dev, git for production).
    /// </summary>
    public async Task EnsureRepositoryAsync(PluginRepositoryDefinition repo, CancellationToken ct = default)
    {
        var existing = await _repository.GetRepositoryAsync(repo.Id, ct).ConfigureAwait(false);
        if (existing is not null)
            return;

        await _repository.UpsertRepositoryAsync(repo, ct).ConfigureAwait(false);
        _logger.LogInformation("Seeded plugin repository '{Id}' ({Url}).", repo.Id, repo.Url);

        // Auto-sync so plugins are immediately available in the store
        await SyncRepositoryAsync(repo.Id, ct).ConfigureAwait(false);
    }

    // ── Repository Management ───────────────────────────────────

    public Task<List<PluginRepositoryDefinition>> GetRepositoriesAsync(CancellationToken ct = default) =>
        _repository.GetRepositoriesAsync(ct);

    public async Task AddRepositoryAsync(PluginRepositoryDefinition repo, CancellationToken ct = default)
    {
        await _repository.UpsertRepositoryAsync(repo, ct).ConfigureAwait(false);
        _logger.LogInformation("Added plugin repository '{Id}' ({Url}).", repo.Id, repo.Url);
    }

    public async Task RemoveRepositoryAsync(string id, CancellationToken ct = default)
    {
        await _repository.DeleteRepositoryAsync(id, ct).ConfigureAwait(false);
        _logger.LogInformation("Removed plugin repository '{Id}'.", id);
    }

    /// <summary>
    /// Fetches the manifest from a repository and caches results in MongoDB.
    /// Delegates to the appropriate <see cref="IPluginRepositorySource"/> by repository type.
    /// </summary>
    public async Task SyncRepositoryAsync(string id, CancellationToken ct = default)
    {
        var repo = await _repository.GetRepositoryAsync(id, ct).ConfigureAwait(false);
        if (repo is null)
        {
            _logger.LogWarning("Repository '{Id}' not found — cannot sync.", id);
            return;
        }

        var source = ResolveSource(repo.Type);
        var manifest = await source.FetchManifestAsync(repo, ct).ConfigureAwait(false);

        repo.CachedPlugins = manifest?.Plugins ?? [];
        repo.LastSyncedAt = DateTime.UtcNow;
        await _repository.UpsertRepositoryAsync(repo, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Synced repository '{Id}' — {Count} plugins found.", id, repo.CachedPlugins.Count);
    }

    // ── Plugin Store (Available Plugins) ────────────────────────

    /// <summary>
    /// Returns all available plugins from all synced repositories, with optional search.
    /// </summary>
    public async Task<List<AvailablePlugin>> GetAvailablePluginsAsync(
        string? query = null, CancellationToken ct = default)
    {
        var repos = await _repository.GetRepositoriesAsync(ct).ConfigureAwait(false);
        var results = new List<AvailablePlugin>();

        foreach (var repo in repos.Where(r => r.Enabled))
        {
            foreach (var plugin in repo.CachedPlugins)
            {
                if (query is not null &&
                    !plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !(plugin.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) &&
                    !plugin.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                results.Add(new AvailablePlugin
                {
                    Id = plugin.Id,
                    Name = plugin.Name,
                    Description = plugin.Description,
                    Version = plugin.Version,
                    Author = plugin.Author,
                    Tags = plugin.Tags,
                    PluginPath = plugin.Path,
                    Homepage = plugin.Homepage,
                    RepositoryId = repo.Id,
                    RepositoryName = repo.Name,
                });
            }
        }

        return results;
    }

    // ── Plugin Installation ─────────────────────────────────────

    /// <summary>
    /// Installs a plugin to <c>plugins/{id}/</c> by delegating to the appropriate
    /// <see cref="IPluginRepositorySource"/> for the source repository.
    /// </summary>
    public async Task InstallPluginAsync(
        string pluginId,
        string repositoryId,
        PluginManifestEntry manifestEntry,
        CancellationToken ct = default)
    {
        var repo = await _repository.GetRepositoryAsync(repositoryId, ct).ConfigureAwait(false);
        if (repo is null)
            throw new InvalidOperationException($"Repository '{repositoryId}' not found.");

        var pluginPath = Path.Combine(_pluginDirectory, pluginId);
        var source = ResolveSource(repo.Type);

        await source.InstallPluginAsync(repo, manifestEntry, pluginPath, ct).ConfigureAwait(false);

        // Update MongoDB cache
        await _repository.UpsertInstalledPluginAsync(new InstalledPluginRecord
        {
            Id = pluginId,
            Name = manifestEntry.Name,
            Version = manifestEntry.Version,
            Source = repositoryId,
            RepositoryId = repositoryId,
            Description = manifestEntry.Description,
            Author = manifestEntry.Author,
            PluginPath = pluginPath,
            Enabled = true,
            InstalledAt = DateTime.UtcNow,
        }, ct).ConfigureAwait(false);

        _changeTracker.MarkRestartRequired();
        _logger.LogInformation("Plugin '{Id}' installed to '{Path}'.", pluginId, pluginPath);
    }

    // ── Plugin State Management ─────────────────────────────────

    /// <summary>
    /// Returns installed plugins from MongoDB, merged with any plugins present on disk
    /// (e.g. baked into the image) that are not yet in the store.
    /// </summary>
    public async Task<List<InstalledPluginRecord>> GetInstalledPluginsAsync(CancellationToken ct = default)
    {
        var fromDb = await _repository.GetInstalledPluginsAsync(ct).ConfigureAwait(false);
        var knownIds = new HashSet<string>(fromDb.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_pluginDirectory))
            return fromDb;

        foreach (var dir in Directory.EnumerateDirectories(_pluginDirectory))
        {
            var pluginCs = Path.Combine(dir, "plugin.cs");
            if (!File.Exists(pluginCs))
                continue;

            var id = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(id) || knownIds.Contains(id))
                continue;

            knownIds.Add(id);
            fromDb.Add(new InstalledPluginRecord
            {
                Id = id,
                Name = id,
                Version = null,
                Source = "bundled",
                RepositoryId = null,
                Description = "Plugin present on disk (e.g. image-bundled).",
                PluginPath = dir,
                Enabled = true,
                InstalledAt = DateTime.UtcNow,
            });
        }

        return fromDb;
    }

    public async Task SetPluginEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
    {
        var record = await _repository.GetInstalledPluginAsync(pluginId, ct).ConfigureAwait(false);
        if (record is not null)
        {
            record.Enabled = enabled;
            await _repository.UpsertInstalledPluginAsync(record, ct).ConfigureAwait(false);
        }

        _changeTracker.MarkRestartRequired();
        _logger.LogInformation("Plugin '{Id}' {State}.", pluginId, enabled ? "enabled" : "disabled");
    }

    public async Task UninstallPluginAsync(string pluginId, CancellationToken ct = default)
    {
        var pluginPath = Path.Combine(_pluginDirectory, pluginId);
        if (Directory.Exists(pluginPath))
            Directory.Delete(pluginPath, recursive: true);

        await _repository.DeleteInstalledPluginAsync(pluginId, ct).ConfigureAwait(false);

        _changeTracker.MarkRestartRequired();
        _logger.LogInformation("Plugin '{Id}' uninstalled.", pluginId);
    }

    // ── Update Detection ────────────────────────────────────────

    /// <summary>
    /// Compares installed plugin versions against cached repository manifests
    /// and returns a list of plugins with available updates.
    /// </summary>
    public async Task<List<PluginUpdateInfo>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var installed = await GetInstalledPluginsAsync(ct).ConfigureAwait(false);
        var repos = await _repository.GetRepositoriesAsync(ct).ConfigureAwait(false);

        var manifestLookup = BuildManifestLookup(repos);
        var updates = new List<PluginUpdateInfo>();

        foreach (var plugin in installed)
        {
            if (!manifestLookup.TryGetValue(plugin.Id, out var entry))
                continue;

            if (!IsNewerVersion(plugin.Version, entry.ManifestEntry.Version))
                continue;

            updates.Add(new PluginUpdateInfo
            {
                PluginId = plugin.Id,
                PluginName = plugin.Name,
                InstalledVersion = plugin.Version,
                AvailableVersion = entry.ManifestEntry.Version,
                RepositoryId = entry.RepositoryId,
            });
        }

        return updates;
    }

    /// <summary>
    /// Updates a plugin to the latest version available in its repository.
    /// Downloads files, updates the installed record, and marks restart required.
    /// </summary>
    public async Task<PluginUpdateResult> UpdatePluginAsync(string pluginId, CancellationToken ct = default)
    {
        var semaphore = _updateLocks.GetOrAdd(pluginId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var record = await _repository.GetInstalledPluginAsync(pluginId, ct).ConfigureAwait(false);
            if (record is null)
            {
                _logger.LogWarning("Plugin '{Id}' is not installed — cannot update.", pluginId);
                return PluginUpdateResult.PluginNotInstalled;
            }

            var repos = await _repository.GetRepositoriesAsync(ct).ConfigureAwait(false);
            var manifestLookup = BuildManifestLookup(repos);

            if (!manifestLookup.TryGetValue(pluginId, out var entry))
            {
                _logger.LogWarning("Plugin '{Id}' not found in any repository — cannot update.", pluginId);
                return PluginUpdateResult.PluginNotInRepository;
            }

            if (!IsNewerVersion(record.Version, entry.ManifestEntry.Version))
            {
                _logger.LogInformation(
                    "Plugin '{Id}' is already up to date at version {Version} — no update performed.",
                    pluginId,
                    record.Version);
                return PluginUpdateResult.AlreadyUpToDate;
            }

            var repo = repos.First(r => r.Id == entry.RepositoryId);
            var source = ResolveSource(repo.Type);
            var pluginPath = Path.Combine(_pluginDirectory, pluginId);

            await source.InstallPluginAsync(repo, entry.ManifestEntry, pluginPath, ct).ConfigureAwait(false);

            var oldVersion = record.Version;
            record.Version = entry.ManifestEntry.Version;

            try
            {
                await _repository.UpsertInstalledPluginAsync(record, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex,
                    "Plugin '{Id}' files updated to {NewVersion} but database update failed. " +
                    "The system is in an inconsistent state — manual intervention may be required.",
                    pluginId, entry.ManifestEntry.Version);
                _changeTracker.MarkRestartRequired();
                throw;
            }

            _changeTracker.MarkRestartRequired();
            _logger.LogInformation(
                "Plugin '{Id}' updated from {OldVersion} to {NewVersion}.",
                pluginId, oldVersion, record.Version);

            return PluginUpdateResult.Updated;
        }
        finally
        {
            semaphore.Release();

            // Remove the semaphore from the dictionary to prevent unbounded growth.
            // A brief window exists where a new caller could create a fresh semaphore,
            // but the operation itself is idempotent so this is safe.
            if (_updateLocks.TryRemove(pluginId, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    /// <summary>
    /// Returns installed plugins enriched with update availability information.
    /// </summary>
    public async Task<List<InstalledPluginWithUpdateInfo>> GetInstalledPluginsWithUpdateInfoAsync(
        CancellationToken ct = default)
    {
        var installed = await GetInstalledPluginsAsync(ct).ConfigureAwait(false);
        var repos = await _repository.GetRepositoriesAsync(ct).ConfigureAwait(false);
        var manifestLookup = BuildManifestLookup(repos);

        return installed.Select(plugin =>
        {
            var hasUpdate = manifestLookup.TryGetValue(plugin.Id, out var entry)
                && IsNewerVersion(plugin.Version, entry.ManifestEntry.Version);

            return new InstalledPluginWithUpdateInfo
            {
                Plugin = plugin,
                UpdateAvailable = hasUpdate,
                AvailableVersion = hasUpdate ? entry!.ManifestEntry.Version : null,
            };
        }).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a lookup from plugin ID → (manifest entry, repository ID) across all enabled repos.
    /// If the same plugin appears in multiple repos, the entry with the highest version wins.
    /// </summary>
    private static Dictionary<string, (PluginManifestEntry ManifestEntry, string RepositoryId)> BuildManifestLookup(
        List<PluginRepositoryDefinition> repos)
    {
        var lookup = new Dictionary<string, (PluginManifestEntry ManifestEntry, string RepositoryId)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var repo in repos.Where(r => r.Enabled))
        {
            foreach (var plugin in repo.CachedPlugins)
            {
                if (lookup.TryGetValue(plugin.Id, out var existing))
                {
                    // A concrete version always wins over a null version
                    if (existing.ManifestEntry.Version is null && plugin.Version is not null)
                    {
                        lookup[plugin.Id] = (plugin, repo.Id);
                    }
                    else if (IsNewerVersion(existing.ManifestEntry.Version, plugin.Version))
                    {
                        lookup[plugin.Id] = (plugin, repo.Id);
                    }

                    continue;
                }

                lookup[plugin.Id] = (plugin, repo.Id);
            }
        }

        return lookup;
    }

    /// <summary>
    /// Returns true if <paramref name="availableVersion"/> is strictly newer than
    /// <paramref name="installedVersion"/>. Returns false for null or unparseable versions.
    /// </summary>
    private static bool IsNewerVersion(string? installedVersion, string? availableVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(availableVersion))
            return false;

        // Strip leading 'v' if present (e.g. "v1.0.0" → "1.0.0")
        var installed = installedVersion.TrimStart('v', 'V');
        var available = availableVersion.TrimStart('v', 'V');

        if (Version.TryParse(installed, out var installedVer) &&
            Version.TryParse(available, out var availableVer))
        {
            return availableVer > installedVer;
        }

        // Fallback: lexicographic comparison for non-standard versions
        return string.Compare(available, installed, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private IPluginRepositorySource ResolveSource(string type)
    {
        if (_sources.TryGetValue(type, out var source))
            return source;

        throw new InvalidOperationException(
            $"No IPluginRepositorySource registered for type '{type}'. " +
            $"Available types: {string.Join(", ", _sources.Keys)}");
    }
}
