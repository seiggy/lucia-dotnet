using System.Text.Json;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed repository for scheduled task persistence.
/// </summary>
public sealed class SqliteScheduledTaskRepository : IScheduledTaskRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SqliteScheduledTaskRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertAsync(ScheduledTaskDocument document, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scheduled_tasks (id, status, fire_at, task_type, data)
            VALUES (@id, @status, @fireAt, @taskType, @data)
            ON CONFLICT(id) DO UPDATE SET status = @status, fire_at = @fireAt, task_type = @taskType, data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", document.Id);
        cmd.Parameters.AddWithValue("@status", document.Status.ToString());
        cmd.Parameters.AddWithValue("@fireAt", document.FireAt.ToString("O"));
        cmd.Parameters.AddWithValue("@taskType", document.TaskType.ToString());
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(document, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ScheduledTaskDocument?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM scheduled_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ScheduledTaskDocument>(json, JsonOptions) : null;
    }

    public async Task<IReadOnlyList<ScheduledTaskDocument>> GetRecoverableTasksAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT data FROM scheduled_tasks
            WHERE status IN (@pending, @active);
            """;
        cmd.Parameters.AddWithValue("@pending", ScheduledTaskStatus.Pending.ToString());
        cmd.Parameters.AddWithValue("@active", ScheduledTaskStatus.Active.ToString());

        return await ReadDocumentsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(string id, ScheduledTaskStatus status, CancellationToken ct = default)
    {
        // Update the denormalized status column AND the JSON data
        var existing = await GetByIdAsync(id, ct).ConfigureAwait(false);
        if (existing is null) return;

        existing.Status = status;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE scheduled_tasks
            SET status = @status, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(existing, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM scheduled_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> PurgeCompletedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = (DateTimeOffset.UtcNow - olderThan).ToString("O");

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM scheduled_tasks
            WHERE status IN (@completed, @dismissed, @autoDismissed, @cancelled, @failed)
              AND fire_at < @cutoff;
            """;
        cmd.Parameters.AddWithValue("@completed", ScheduledTaskStatus.Completed.ToString());
        cmd.Parameters.AddWithValue("@dismissed", ScheduledTaskStatus.Dismissed.ToString());
        cmd.Parameters.AddWithValue("@autoDismissed", ScheduledTaskStatus.AutoDismissed.ToString());
        cmd.Parameters.AddWithValue("@cancelled", ScheduledTaskStatus.Cancelled.ToString());
        cmd.Parameters.AddWithValue("@failed", ScheduledTaskStatus.Failed.ToString());
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ScheduledTaskDocument>> ReadDocumentsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<ScheduledTaskDocument>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var doc = JsonSerializer.Deserialize<ScheduledTaskDocument>(json, JsonOptions);
            if (doc is not null)
            {
                results.Add(doc);
            }
        }

        return results;
    }
}
