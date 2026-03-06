using lucia.TimerAgent.ScheduledTasks;

using Microsoft.EntityFrameworkCore;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IScheduledTaskRepository"/>.
/// </summary>
public sealed class EfScheduledTaskRepository(IDbContextFactory<LuciaDbContext> dbFactory) : IScheduledTaskRepository
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    public async Task UpsertAsync(ScheduledTaskDocument document, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.ScheduledTasks
            .FindAsync([document.Id], ct)
            .ConfigureAwait(false);

        if (existing is not null)
            db.Entry(existing).CurrentValues.SetValues(document);
        else
            db.ScheduledTasks.Add(document);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ScheduledTaskDocument?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ScheduledTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScheduledTaskDocument>> GetRecoverableTasksAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.ScheduledTasks
            .AsNoTracking()
            .Where(d => d.Status == ScheduledTaskStatus.Pending || d.Status == ScheduledTaskStatus.Active)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(string id, ScheduledTaskStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var task = await db.ScheduledTasks
            .FindAsync([id], ct)
            .ConfigureAwait(false);

        if (task is null)
            return;

        task.Status = status;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var task = await db.ScheduledTasks
            .FindAsync([id], ct)
            .ConfigureAwait(false);

        if (task is null)
            return;

        db.ScheduledTasks.Remove(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> PurgeCompletedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var cutoff = DateTimeOffset.UtcNow - olderThan;

        var toDelete = await db.ScheduledTasks
            .Where(d =>
                (d.Status == ScheduledTaskStatus.Completed ||
                 d.Status == ScheduledTaskStatus.Dismissed ||
                 d.Status == ScheduledTaskStatus.AutoDismissed ||
                 d.Status == ScheduledTaskStatus.Cancelled ||
                 d.Status == ScheduledTaskStatus.Failed) &&
                d.FireAt < cutoff)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (toDelete.Count == 0)
            return 0;

        db.ScheduledTasks.RemoveRange(toDelete);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return toDelete.Count;
    }
}
