using System.Text.Json;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite implementation for alarm clock definitions and alarm sound catalog.
/// </summary>
public sealed class SqliteAlarmClockRepository : IAlarmClockRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SqliteAlarmClockRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // -- Alarm Clock CRUD --

    public async Task<IReadOnlyList<AlarmClock>> GetAllAlarmsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM alarm_clocks;";

        return await ReadAlarmsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<AlarmClock?> GetAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM alarm_clocks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", alarmId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<AlarmClock>(json, JsonOptions) : null;
    }

    public async Task UpsertAlarmAsync(AlarmClock alarm, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alarm_clocks (id, is_enabled, next_fire_at, data)
            VALUES (@id, @isEnabled, @nextFireAt, @data)
            ON CONFLICT(id) DO UPDATE SET is_enabled = @isEnabled, next_fire_at = @nextFireAt, data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", alarm.Id);
        cmd.Parameters.AddWithValue("@isEnabled", alarm.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@nextFireAt", alarm.NextFireAt.HasValue ? alarm.NextFireAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(alarm, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM alarm_clocks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", alarmId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlarmClock>> GetDueAlarmsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT data FROM alarm_clocks
            WHERE is_enabled = 1 AND next_fire_at IS NOT NULL AND next_fire_at <= @now;
            """;
        cmd.Parameters.AddWithValue("@now", now.ToString("O"));

        return await ReadAlarmsAsync(cmd, ct).ConfigureAwait(false);
    }

    // -- Alarm Sound catalog --

    public async Task<IReadOnlyList<AlarmSound>> GetAllSoundsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM alarm_sounds;";

        return await ReadSoundsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetSoundAsync(string soundId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM alarm_sounds WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", soundId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<AlarmSound>(json, JsonOptions) : null;
    }

    public async Task UpsertSoundAsync(AlarmSound sound, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alarm_sounds (id, is_default, data)
            VALUES (@id, @isDefault, @data)
            ON CONFLICT(id) DO UPDATE SET is_default = @isDefault, data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", sound.Id);
        cmd.Parameters.AddWithValue("@isDefault", sound.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(sound, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteSoundAsync(string soundId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM alarm_sounds WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", soundId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetDefaultSoundAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM alarm_sounds WHERE is_default = 1 LIMIT 1;";

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<AlarmSound>(json, JsonOptions) : null;
    }

    private static async Task<IReadOnlyList<AlarmClock>> ReadAlarmsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<AlarmClock>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var alarm = JsonSerializer.Deserialize<AlarmClock>(json, JsonOptions);
            if (alarm is not null)
            {
                results.Add(alarm);
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<AlarmSound>> ReadSoundsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<AlarmSound>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var sound = JsonSerializer.Deserialize<AlarmSound>(json, JsonOptions);
            if (sound is not null)
            {
                results.Add(sound);
            }
        }

        return results;
    }
}
