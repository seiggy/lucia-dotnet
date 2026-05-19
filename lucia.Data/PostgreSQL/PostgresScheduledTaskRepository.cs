using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.TimerAgent.ScheduledTasks;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed repository for scheduled task persistence.
/// </summary>
public sealed class PostgresScheduledTaskRepository : IScheduledTaskRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresScheduledTaskRepository([FromKeyedServices(PostgresDbNames.Tasks)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(ScheduledTaskDocument document, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scheduled_tasks (id, status, fire_at, task_type, data)
            VALUES (@id, @status, @fireAt, @taskType, @data)
            ON CONFLICT (id) DO UPDATE SET
                status = EXCLUDED.status,
                fire_at = EXCLUDED.fire_at,
                task_type = EXCLUDED.task_type,
                data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", document.Id);
        cmd.Parameters.AddWithValue("status", document.Status.ToString());
        cmd.Parameters.AddWithValue("fireAt", document.FireAt);
        cmd.Parameters.AddWithValue("taskType", document.TaskType.ToString());
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(document, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ScheduledTaskDocument?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM scheduled_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ScheduledTaskDocument>(json, JsonOptions) : null;
    }

    public async Task<IReadOnlyList<ScheduledTaskDocument>> GetRecoverableTasksAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT data::text FROM scheduled_tasks
            WHERE status IN (@pending, @active);
            """;
        cmd.Parameters.AddWithValue("pending", ScheduledTaskStatus.Pending.ToString());
        cmd.Parameters.AddWithValue("active", ScheduledTaskStatus.Active.ToString());

        return await ReadDocumentsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(string id, ScheduledTaskStatus status, CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        existing.Status = status;

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE scheduled_tasks
            SET status = @status, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status.ToString());
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(existing, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM scheduled_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> PurgeCompletedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - olderThan;

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM scheduled_tasks
            WHERE status IN (@completed, @dismissed, @autoDismissed, @cancelled, @failed)
              AND fire_at < @cutoff;
            """;
        cmd.Parameters.AddWithValue("completed", ScheduledTaskStatus.Completed.ToString());
        cmd.Parameters.AddWithValue("dismissed", ScheduledTaskStatus.Dismissed.ToString());
        cmd.Parameters.AddWithValue("autoDismissed", ScheduledTaskStatus.AutoDismissed.ToString());
        cmd.Parameters.AddWithValue("cancelled", ScheduledTaskStatus.Cancelled.ToString());
        cmd.Parameters.AddWithValue("failed", ScheduledTaskStatus.Failed.ToString());
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ScheduledTaskDocument>> ReadDocumentsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<ScheduledTaskDocument>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var document = JsonSerializer.Deserialize<ScheduledTaskDocument>(reader.GetString(0), JsonOptions);
            if (document is not null)
            {
                results.Add(document);
            }
        }

        return results;
    }
}
