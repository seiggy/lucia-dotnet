using lucia.TimerAgent.ScheduledTasks;

using Microsoft.EntityFrameworkCore;

namespace lucia.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAlarmClockRepository"/>.
/// </summary>
public sealed class EfAlarmClockRepository(IDbContextFactory<LuciaDbContext> dbFactory) : IAlarmClockRepository
{
    private readonly IDbContextFactory<LuciaDbContext> _dbFactory = dbFactory;

    // -- Alarm Clock CRUD --

    public async Task<IReadOnlyList<AlarmClock>> GetAllAlarmsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AlarmClocks
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<AlarmClock?> GetAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AlarmClocks
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == alarmId, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertAlarmAsync(AlarmClock alarm, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.AlarmClocks
            .FindAsync([alarm.Id], ct)
            .ConfigureAwait(false);

        if (existing is not null)
            db.Entry(existing).CurrentValues.SetValues(alarm);
        else
            db.AlarmClocks.Add(alarm);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var alarm = await db.AlarmClocks
            .FindAsync([alarmId], ct)
            .ConfigureAwait(false);

        if (alarm is null)
            return;

        db.AlarmClocks.Remove(alarm);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlarmClock>> GetDueAlarmsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AlarmClocks
            .AsNoTracking()
            .Where(a => a.IsEnabled && a.NextFireAt != null && a.NextFireAt <= now)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    // -- Alarm Sound catalog --

    public async Task<IReadOnlyList<AlarmSound>> GetAllSoundsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AlarmSounds
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetSoundAsync(string soundId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AlarmSounds
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == soundId, ct)
            .ConfigureAwait(false);
    }

    public async Task UpsertSoundAsync(AlarmSound sound, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.AlarmSounds
            .FindAsync([sound.Id], ct)
            .ConfigureAwait(false);

        if (existing is not null)
            db.Entry(existing).CurrentValues.SetValues(sound);
        else
            db.AlarmSounds.Add(sound);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteSoundAsync(string soundId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var sound = await db.AlarmSounds
            .FindAsync([soundId], ct)
            .ConfigureAwait(false);

        if (sound is null)
            return;

        db.AlarmSounds.Remove(sound);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetDefaultSoundAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.AlarmSounds
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsDefault, ct)
            .ConfigureAwait(false);
    }
}
