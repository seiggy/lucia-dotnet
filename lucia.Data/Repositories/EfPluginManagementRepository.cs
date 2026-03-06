using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.PluginFramework;

using Microsoft.EntityFrameworkCore;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPluginManagementRepository"/>.
/// Manages both <see cref="PluginRepositoryDefinition"/> and <see cref="InstalledPluginRecord"/> entities.
/// </summary>
public sealed class EfPluginManagementRepository(IDbContextFactory<LuciaDbContext> dbFactory) : IPluginManagementRepository
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    // ── Plugin Repositories ─────────────────────────────────────

    public async Task<List<PluginRepositoryDefinition>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PluginRepositories
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<PluginRepositoryDefinition?> GetRepositoryAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PluginRepositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertRepositoryAsync(PluginRepositoryDefinition repo, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.PluginRepositories.FindAsync([repo.Id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Entry(existing).CurrentValues.SetValues(repo);
        }
        else
        {
            db.PluginRepositories.Add(repo);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteRepositoryAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.PluginRepositories.FindAsync([id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.PluginRepositories.Remove(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    // ── Installed Plugins ───────────────────────────────────────

    public async Task<List<InstalledPluginRecord>> GetInstalledPluginsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.InstalledPlugins
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<InstalledPluginRecord?> GetInstalledPluginAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.InstalledPlugins
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertInstalledPluginAsync(InstalledPluginRecord record, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.InstalledPlugins.FindAsync([record.Id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Entry(existing).CurrentValues.SetValues(record);
        }
        else
        {
            db.InstalledPlugins.Add(record);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteInstalledPluginAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.InstalledPlugins.FindAsync([id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.InstalledPlugins.Remove(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
