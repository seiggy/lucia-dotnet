using System.Text.Json;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.Agents.Training.Models;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed durable archive for completed agent tasks.
/// </summary>
public sealed class SqliteTaskArchiveStore : ITaskArchiveStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SqliteTaskArchiveStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ArchiveTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        var archived = MapToArchived(task);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO archived_tasks (id, archived_at, final_state, data)
            VALUES (@id, @archivedAt, @finalState, @data)
            ON CONFLICT(id) DO UPDATE SET archived_at = @archivedAt, final_state = @finalState, data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", archived.Id);
        cmd.Parameters.AddWithValue("@archivedAt", archived.ArchivedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@finalState", archived.Status);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(archived, JsonOptions));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArchivedTask?> GetArchivedTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM archived_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", taskId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ArchivedTask>(json, JsonOptions) : null;
    }

    public async Task<PagedResult<ArchivedTask>> ListArchivedTasksAsync(TaskFilterCriteria filter, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var (whereClause, parameters) = BuildFilter(filter);

        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM archived_tasks{whereClause};";
        foreach (var p in parameters)
        {
            countCmd.Parameters.AddWithValue(p.Key, p.Value);
        }

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

        var skip = (filter.Page - 1) * filter.PageSize;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT data FROM archived_tasks{whereClause} ORDER BY archived_at DESC LIMIT @limit OFFSET @offset;";
        foreach (var p in parameters)
        {
            cmd.Parameters.AddWithValue(p.Key, p.Value);
        }
        cmd.Parameters.AddWithValue("@limit", filter.PageSize);
        cmd.Parameters.AddWithValue("@offset", skip);

        var items = new List<ArchivedTask>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var task = JsonSerializer.Deserialize<ArchivedTask>(json, JsonOptions);
            if (task is not null)
            {
                items.Add(task);
            }
        }

        return new PagedResult<ArchivedTask>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task<TaskStats> GetTaskStatsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        using var statsCmd = connection.CreateCommand();
        statsCmd.CommandText = """
            SELECT
                COUNT(*) AS total,
                SUM(CASE WHEN final_state = 'Completed' THEN 1 ELSE 0 END) AS completed,
                SUM(CASE WHEN final_state = 'Failed' THEN 1 ELSE 0 END) AS failed,
                SUM(CASE WHEN final_state = 'Canceled' THEN 1 ELSE 0 END) AS canceled
            FROM archived_tasks;
            """;

        int total = 0, completed = 0, failed = 0, canceled = 0;
        using (var reader = await statsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                total = Convert.ToInt32(reader["total"]);
                completed = Convert.ToInt32(reader["completed"]);
                failed = Convert.ToInt32(reader["failed"]);
                canceled = Convert.ToInt32(reader["canceled"]);
            }
        }

        // Aggregate by agent from JSON data
        var byAgent = new Dictionary<string, int>();
        using var agentCmd = connection.CreateCommand();
        agentCmd.CommandText = "SELECT data FROM archived_tasks;";
        using (var reader = await agentCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var json = reader.GetString(0);
                var task = JsonSerializer.Deserialize<ArchivedTask>(json, JsonOptions);
                if (task?.AgentIds is null) continue;

                foreach (var agentId in task.AgentIds)
                {
                    if (!string.IsNullOrEmpty(agentId))
                    {
                        byAgent[agentId] = byAgent.GetValueOrDefault(agentId) + 1;
                    }
                }
            }
        }

        return new TaskStats
        {
            TotalTasks = total,
            CompletedCount = completed,
            FailedCount = failed,
            CanceledCount = canceled,
            ByAgent = byAgent,
        };
    }

    public async Task<bool> IsArchivedAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM archived_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", taskId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count > 0;
    }

    private static ArchivedTask MapToArchived(AgentTask task)
    {
        var history = task.History ?? [];
        var agentIds = history
            .Where(m => m.Role == MessageRole.Agent)
            .Select(m => m.Extensions?.FirstOrDefault() ?? "unknown")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var userInput = history
            .Where(m => m.Role == MessageRole.User)
            .SelectMany(m => m.Parts?.OfType<TextPart>() ?? [])
            .FirstOrDefault()?.Text;

        var finalResponse = history
            .Where(m => m.Role == MessageRole.Agent)
            .SelectMany(m => m.Parts?.OfType<TextPart>() ?? [])
            .LastOrDefault()?.Text;

        var messages = history.Select(m => new ArchivedMessage
        {
            Role = m.Role.ToString(),
            Text = string.Join(' ', m.Parts?.OfType<TextPart>().Select(p => p.Text) ?? []),
            MessageId = m.MessageId,
        }).ToList();

        return new ArchivedTask
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = task.Status.State.ToString(),
            AgentIds = agentIds,
            UserInput = userInput,
            FinalResponse = finalResponse,
            MessageCount = history.Count,
            History = messages,
            CreatedAt = task.Status.Timestamp.UtcDateTime,
            ArchivedAt = DateTime.UtcNow,
        };
    }

    private static (string WhereClause, Dictionary<string, object> Parameters) BuildFilter(TaskFilterCriteria criteria)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(criteria.Status))
        {
            clauses.Add("final_state = @status");
            parameters["@status"] = criteria.Status;
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentId))
        {
            // Search for agentId inside the JSON agentIds array
            clauses.Add("data LIKE @agentId");
            parameters["@agentId"] = $"%\"{criteria.AgentId}\"%";
        }

        if (criteria.FromDate.HasValue)
        {
            clauses.Add("archived_at >= @fromDate");
            parameters["@fromDate"] = criteria.FromDate.Value.ToString("O");
        }

        if (criteria.ToDate.HasValue)
        {
            clauses.Add("archived_at <= @toDate");
            parameters["@toDate"] = criteria.ToDate.Value.ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            clauses.Add("data LIKE @search");
            parameters["@search"] = $"%{criteria.Search}%";
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        return (whereClause, parameters);
    }
}
