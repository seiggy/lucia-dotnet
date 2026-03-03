using lucia.Agents.Configuration;
using lucia.Agents.PluginFramework;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Data access for plugin repository definitions and installed plugin records.
/// </summary>
public interface IPluginManagementRepository
{
    // ── Plugin Repositories ─────────────────────────────────────
    Task<List<PluginRepositoryDefinition>> GetRepositoriesAsync(CancellationToken ct = default);
    Task<PluginRepositoryDefinition?> GetRepositoryAsync(string id, CancellationToken ct = default);
    Task UpsertRepositoryAsync(PluginRepositoryDefinition repo, CancellationToken ct = default);
    Task DeleteRepositoryAsync(string id, CancellationToken ct = default);

    // ── Installed Plugins ───────────────────────────────────────
    Task<List<InstalledPluginRecord>> GetInstalledPluginsAsync(CancellationToken ct = default);
    Task<InstalledPluginRecord?> GetInstalledPluginAsync(string id, CancellationToken ct = default);
    Task UpsertInstalledPluginAsync(InstalledPluginRecord record, CancellationToken ct = default);
    Task DeleteInstalledPluginAsync(string id, CancellationToken ct = default);
}
