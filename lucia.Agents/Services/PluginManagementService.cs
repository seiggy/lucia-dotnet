using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

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

    public Task<List<InstalledPluginRecord>> GetInstalledPluginsAsync(CancellationToken ct = default) =>
        _repository.GetInstalledPluginsAsync(ct);

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

    // ── Helpers ──────────────────────────────────────────────────

    private IPluginRepositorySource ResolveSource(string type)
    {
        if (_sources.TryGetValue(type, out var source))
            return source;

        throw new InvalidOperationException(
            $"No IPluginRepositorySource registered for type '{type}'. " +
            $"Available types: {string.Join(", ", _sources.Keys)}");
    }
}
