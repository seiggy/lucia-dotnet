using lucia.Agents.Abstractions;
using lucia.Agents.Models.HomeAssistant;
using lucia.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPresenceSensorRepository"/>.
/// Manages presence sensor mappings and the global enabled/disabled configuration.
/// </summary>
public sealed class EfPresenceSensorRepository(IDbContextFactory<LuciaDbContext> dbFactory) : IPresenceSensorRepository
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    public async Task<IReadOnlyList<PresenceSensorMapping>> GetAllMappingsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.PresenceSensorMappings
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task ReplaceAutoDetectedMappingsAsync(
        IReadOnlyList<PresenceSensorMapping> autoDetected,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // Delete all non-user-override mappings
        var existing = await db.PresenceSensorMappings
            .Where(m => !m.IsUserOverride)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        db.PresenceSensorMappings.RemoveRange(existing);

        if (autoDetected.Count > 0)
        {
            db.PresenceSensorMappings.AddRange(autoDetected);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertMappingAsync(PresenceSensorMapping mapping, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.PresenceSensorMappings
            .FindAsync([mapping.EntityId], ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            db.Entry(existing).CurrentValues.SetValues(mapping);
        }
        else
        {
            db.PresenceSensorMappings.Add(mapping);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteMappingAsync(string entityId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.PresenceSensorMappings
            .FindAsync([entityId], ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            db.PresenceSensorMappings.Remove(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task<bool> GetEnabledAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entry = await db.PresenceConfig
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Key == PresenceConfigEntry.EnabledKey, ct)
            .ConfigureAwait(false);

        return entry?.Enabled ?? true; // enabled by default
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.PresenceConfig
            .FindAsync([PresenceConfigEntry.EnabledKey], ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Enabled = enabled;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            db.PresenceConfig.Add(new PresenceConfigEntry
            {
                Key = PresenceConfigEntry.EnabledKey,
                Enabled = enabled,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
