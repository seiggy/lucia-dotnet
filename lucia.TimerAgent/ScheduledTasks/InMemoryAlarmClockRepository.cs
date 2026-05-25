namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// In-memory fallback implementation of <see cref="IAlarmClockRepository"/>.
/// </summary>
public sealed class InMemoryAlarmClockRepository : IAlarmClockRepository
{
    private readonly Dictionary<string, AlarmClock> _alarms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AlarmSound> _sounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public Task<IReadOnlyList<AlarmClock>> GetAllAlarmsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<AlarmClock> alarms = _alarms.Values
                .OrderBy(alarm => alarm.NextFireAt)
                .ToList();

            return Task.FromResult(alarms);
        }
    }

    public Task<AlarmClock?> GetAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _alarms.TryGetValue(alarmId, out var alarm);
            return Task.FromResult(alarm);
        }
    }

    public Task UpsertAlarmAsync(AlarmClock alarm, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _alarms[alarm.Id] = alarm;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _alarms.Remove(alarmId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlarmClock>> GetDueAlarmsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<AlarmClock> alarms = _alarms.Values
                .Where(alarm => alarm.IsEnabled && alarm.NextFireAt.HasValue && alarm.NextFireAt.Value <= now)
                .OrderBy(alarm => alarm.NextFireAt)
                .ToList();

            return Task.FromResult(alarms);
        }
    }

    public Task<IReadOnlyList<AlarmSound>> GetAllSoundsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<AlarmSound> sounds = _sounds.Values
                .OrderBy(sound => sound.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(sounds);
        }
    }

    public Task<AlarmSound?> GetSoundAsync(string soundId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _sounds.TryGetValue(soundId, out var sound);
            return Task.FromResult(sound);
        }
    }

    public Task UpsertSoundAsync(AlarmSound sound, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (sound.IsDefault)
            {
                foreach (var existingSound in _sounds.Values)
                {
                    existingSound.IsDefault = false;
                }
            }

            _sounds[sound.Id] = sound;
        }

        return Task.CompletedTask;
    }

    public Task DeleteSoundAsync(string soundId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _sounds.Remove(soundId);
        }

        return Task.CompletedTask;
    }

    public Task<AlarmSound?> GetDefaultSoundAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var sound = _sounds.Values.FirstOrDefault(existing => existing.IsDefault);
            return Task.FromResult(sound);
        }
    }
}
