using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using MongoDB.Driver;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// MongoDB-backed implementation of <see cref="IPluginManagementRepository"/>.
/// Stores plugin repository definitions and installed plugin records.
/// </summary>
public sealed class MongoPluginManagementRepository : IPluginManagementRepository
{
    private readonly IMongoCollection<PluginRepositoryDefinition> _repos;
    private readonly IMongoCollection<InstalledPluginRecord> _installed;

    public MongoPluginManagementRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(ConfigEntry.DatabaseName);
        _repos = db.GetCollection<PluginRepositoryDefinition>(PluginRepositoryDefinition.CollectionName);
        _installed = db.GetCollection<InstalledPluginRecord>(InstalledPluginRecord.CollectionName);
    }

    // ── Repositories ────────────────────────────────────────────

    public async Task<List<PluginRepositoryDefinition>> GetRepositoriesAsync(CancellationToken ct = default) =>
        await _repos.Find(_ => true).ToListAsync(ct).ConfigureAwait(false);

    public async Task<PluginRepositoryDefinition?> GetRepositoryAsync(string id, CancellationToken ct = default) =>
        await _repos.Find(r => r.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task UpsertRepositoryAsync(PluginRepositoryDefinition repo, CancellationToken ct = default) =>
        await _repos.ReplaceOneAsync(
            r => r.Id == repo.Id,
            repo,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);

    public async Task DeleteRepositoryAsync(string id, CancellationToken ct = default) =>
        await _repos.DeleteOneAsync(r => r.Id == id, ct).ConfigureAwait(false);

    // ── Installed Plugins ───────────────────────────────────────

    public async Task<List<InstalledPluginRecord>> GetInstalledPluginsAsync(CancellationToken ct = default) =>
        await _installed.Find(_ => true).ToListAsync(ct).ConfigureAwait(false);

    public async Task<InstalledPluginRecord?> GetInstalledPluginAsync(string id, CancellationToken ct = default) =>
        await _installed.Find(p => p.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task UpsertInstalledPluginAsync(InstalledPluginRecord record, CancellationToken ct = default) =>
        await _installed.ReplaceOneAsync(
            p => p.Id == record.Id,
            record,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);

    public async Task DeleteInstalledPluginAsync(string id, CancellationToken ct = default) =>
        await _installed.DeleteOneAsync(p => p.Id == id, ct).ConfigureAwait(false);
}
