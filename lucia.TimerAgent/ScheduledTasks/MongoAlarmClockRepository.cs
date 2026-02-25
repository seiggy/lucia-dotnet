using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// MongoDB implementation for alarm clock definitions and alarm sound catalog.
/// Uses the "luciatasks" database with "alarm_clocks" and "alarm_sounds" collections.
/// </summary>
public sealed class MongoAlarmClockRepository : IAlarmClockRepository
{
    private const string DatabaseName = "luciatasks";
    private const string AlarmsCollectionName = "alarm_clocks";
    private const string SoundsCollectionName = "alarm_sounds";

    private readonly IMongoCollection<AlarmClock> _alarms;
    private readonly IMongoCollection<AlarmSound> _sounds;

    public MongoAlarmClockRepository(IMongoClient mongoClient)
    {
        var db = mongoClient.GetDatabase(DatabaseName);
        _alarms = db.GetCollection<AlarmClock>(AlarmsCollectionName);
        _sounds = db.GetCollection<AlarmSound>(SoundsCollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _alarms.Indexes.CreateMany([
            new CreateIndexModel<AlarmClock>(
                Builders<AlarmClock>.IndexKeys.Ascending(a => a.IsEnabled)),
            new CreateIndexModel<AlarmClock>(
                Builders<AlarmClock>.IndexKeys.Ascending(a => a.NextFireAt)),
        ]);

        _sounds.Indexes.CreateMany([
            new CreateIndexModel<AlarmSound>(
                Builders<AlarmSound>.IndexKeys.Ascending(s => s.IsDefault)),
        ]);
    }

    // -- Alarm Clock CRUD --

    public async Task<IReadOnlyList<AlarmClock>> GetAllAlarmsAsync(CancellationToken ct = default)
    {
        var cursor = await _alarms.FindAsync(
            Builders<AlarmClock>.Filter.Empty, cancellationToken: ct).ConfigureAwait(false);
        return await cursor.ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<AlarmClock?> GetAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        var cursor = await _alarms.FindAsync(
            Builders<AlarmClock>.Filter.Eq(a => a.Id, alarmId),
            cancellationToken: ct).ConfigureAwait(false);
        return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertAlarmAsync(AlarmClock alarm, CancellationToken ct = default)
    {
        await _alarms.ReplaceOneAsync(
            Builders<AlarmClock>.Filter.Eq(a => a.Id, alarm.Id),
            alarm,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    public async Task DeleteAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        await _alarms.DeleteOneAsync(
            Builders<AlarmClock>.Filter.Eq(a => a.Id, alarmId),
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlarmClock>> GetDueAlarmsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var filter = Builders<AlarmClock>.Filter.And(
            Builders<AlarmClock>.Filter.Eq(a => a.IsEnabled, true),
            Builders<AlarmClock>.Filter.Lte(a => a.NextFireAt, now));

        var cursor = await _alarms.FindAsync(filter, cancellationToken: ct).ConfigureAwait(false);
        return await cursor.ToListAsync(ct).ConfigureAwait(false);
    }

    // -- Alarm Sound catalog --

    public async Task<IReadOnlyList<AlarmSound>> GetAllSoundsAsync(CancellationToken ct = default)
    {
        var cursor = await _sounds.FindAsync(
            Builders<AlarmSound>.Filter.Empty, cancellationToken: ct).ConfigureAwait(false);
        return await cursor.ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetSoundAsync(string soundId, CancellationToken ct = default)
    {
        var cursor = await _sounds.FindAsync(
            Builders<AlarmSound>.Filter.Eq(s => s.Id, soundId),
            cancellationToken: ct).ConfigureAwait(false);
        return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertSoundAsync(AlarmSound sound, CancellationToken ct = default)
    {
        await _sounds.ReplaceOneAsync(
            Builders<AlarmSound>.Filter.Eq(s => s.Id, sound.Id),
            sound,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);
    }

    public async Task DeleteSoundAsync(string soundId, CancellationToken ct = default)
    {
        await _sounds.DeleteOneAsync(
            Builders<AlarmSound>.Filter.Eq(s => s.Id, soundId),
            ct).ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetDefaultSoundAsync(CancellationToken ct = default)
    {
        var cursor = await _sounds.FindAsync(
            Builders<AlarmSound>.Filter.Eq(s => s.IsDefault, true),
            cancellationToken: ct).ConfigureAwait(false);
        return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }
}
