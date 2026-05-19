using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.TimerAgent.ScheduledTasks;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL implementation for alarm clock definitions and alarm sound catalog.
/// </summary>
public sealed class PostgresAlarmClockRepository : IAlarmClockRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresAlarmClockRepository([FromKeyedServices(PostgresDbNames.Tasks)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AlarmClock>> GetAllAlarmsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM alarm_clocks;";

        return await ReadAlarmsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<AlarmClock?> GetAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM alarm_clocks WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", alarmId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<AlarmClock>(json, JsonOptions) : null;
    }

    public async Task UpsertAlarmAsync(AlarmClock alarm, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alarm_clocks (id, is_enabled, next_fire_at, data)
            VALUES (@id, @isEnabled, @nextFireAt, @data)
            ON CONFLICT (id) DO UPDATE SET
                is_enabled = EXCLUDED.is_enabled,
                next_fire_at = EXCLUDED.next_fire_at,
                data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", alarm.Id);
        cmd.Parameters.AddWithValue("isEnabled", alarm.IsEnabled);
        cmd.Parameters.AddWithValue("nextFireAt", (object?)alarm.NextFireAt ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(alarm, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAlarmAsync(string alarmId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM alarm_clocks WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", alarmId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlarmClock>> GetDueAlarmsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT data::text FROM alarm_clocks
            WHERE is_enabled = TRUE AND next_fire_at IS NOT NULL AND next_fire_at <= @now;
            """;
        cmd.Parameters.AddWithValue("now", now);

        return await ReadAlarmsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AlarmSound>> GetAllSoundsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM alarm_sounds;";

        return await ReadSoundsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetSoundAsync(string soundId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM alarm_sounds WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", soundId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<AlarmSound>(json, JsonOptions) : null;
    }

    public async Task UpsertSoundAsync(AlarmSound sound, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alarm_sounds (id, is_default, data)
            VALUES (@id, @isDefault, @data)
            ON CONFLICT (id) DO UPDATE SET
                is_default = EXCLUDED.is_default,
                data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", sound.Id);
        cmd.Parameters.AddWithValue("isDefault", sound.IsDefault);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(sound, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteSoundAsync(string soundId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM alarm_sounds WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", soundId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<AlarmSound?> GetDefaultSoundAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM alarm_sounds WHERE is_default = TRUE LIMIT 1;";

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<AlarmSound>(json, JsonOptions) : null;
    }

    private static async Task<IReadOnlyList<AlarmClock>> ReadAlarmsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<AlarmClock>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var alarm = JsonSerializer.Deserialize<AlarmClock>(reader.GetString(0), JsonOptions);
            if (alarm is not null)
            {
                results.Add(alarm);
            }
        }

        return results;
    }

    private static async Task<IReadOnlyList<AlarmSound>> ReadSoundsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<AlarmSound>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var sound = JsonSerializer.Deserialize<AlarmSound>(reader.GetString(0), JsonOptions);
            if (sound is not null)
            {
                results.Add(sound);
            }
        }

        return results;
    }
}
