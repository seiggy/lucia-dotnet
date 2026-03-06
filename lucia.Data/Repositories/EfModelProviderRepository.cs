using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;

using Microsoft.EntityFrameworkCore;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IModelProviderRepository"/>.
/// </summary>
public sealed class EfModelProviderRepository(IDbContextFactory<LuciaDbContext> dbFactory) : IModelProviderRepository
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    public async Task<List<ModelProvider>> GetAllProvidersAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ModelProviders
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<List<ModelProvider>> GetEnabledProvidersAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ModelProviders
            .AsNoTracking()
            .Where(p => p.Enabled)
            .OrderBy(p => p.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ModelProvider?> GetProviderAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ModelProviders
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertProviderAsync(ModelProvider provider, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        provider.UpdatedAt = DateTime.UtcNow;
        var existing = await db.ModelProviders.FindAsync([provider.Id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.Entry(existing).CurrentValues.SetValues(provider);
        }
        else
        {
            db.ModelProviders.Add(provider);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteProviderAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.ModelProviders.FindAsync([id], ct).ConfigureAwait(false);
        if (existing is not null)
        {
            db.ModelProviders.Remove(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
