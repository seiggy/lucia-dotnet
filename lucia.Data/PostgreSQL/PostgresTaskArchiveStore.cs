using System.Text.Json;
using System.Text.Json.Serialization;

using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.Agents.Training.Models;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed durable archive for completed agent tasks.
/// </summary>
public sealed class PostgresTaskArchiveStore : ITaskArchiveStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresTaskArchiveStore([FromKeyedServices(PostgresDbNames.Tasks)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ArchiveTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        var archived = MapToArchived(task);
        var json = JsonSerializer.Serialize(archived, JsonOptions);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO archived_tasks (id, archived_at, final_state, data)
            VALUES (@id, @archivedAt, @finalState, @data)
            ON CONFLICT (id) DO UPDATE SET
                archived_at = EXCLUDED.archived_at,
                final_state = EXCLUDED.final_state,
                data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", archived.Id);
        cmd.Parameters.AddWithValue("archivedAt", archived.ArchivedAt);
        cmd.Parameters.AddWithValue("finalState", archived.Status);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = json });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArchivedTask?> GetArchivedTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM archived_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", taskId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ArchivedTask>(json, JsonOptions) : null;
    }

    public async Task<PagedResult<ArchivedTask>> ListArchivedTasksAsync(TaskFilterCriteria filter, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        var (whereClause, parameters) = BuildFilter(filter);

        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM archived_tasks{whereClause};";
        foreach (var parameter in parameters)
        {
            countCmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        var skip = (filter.Page - 1) * filter.PageSize;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT data::text FROM archived_tasks{whereClause} ORDER BY archived_at DESC LIMIT @limit OFFSET @offset;";
        foreach (var parameter in parameters)
        {
            cmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        cmd.Parameters.AddWithValue("limit", filter.PageSize);
        cmd.Parameters.AddWithValue("offset", skip);

        var items = new List<ArchivedTask>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var task = JsonSerializer.Deserialize<ArchivedTask>(reader.GetString(0), JsonOptions);
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
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using var statsCmd = connection.CreateCommand();
        statsCmd.CommandText = """
            SELECT
                COUNT(*) AS total,
                SUM(CASE WHEN final_state = 'Completed' THEN 1 ELSE 0 END) AS completed,
                SUM(CASE WHEN final_state = 'Failed' THEN 1 ELSE 0 END) AS failed,
                SUM(CASE WHEN final_state = 'Canceled' THEN 1 ELSE 0 END) AS canceled
            FROM archived_tasks;
            """;

        var total = 0;
        var completed = 0;
        var failed = 0;
        var canceled = 0;

        await using (var reader = await statsCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                total = reader["total"] is DBNull ? 0 : Convert.ToInt32(reader["total"]);
                completed = reader["completed"] is DBNull ? 0 : Convert.ToInt32(reader["completed"]);
                failed = reader["failed"] is DBNull ? 0 : Convert.ToInt32(reader["failed"]);
                canceled = reader["canceled"] is DBNull ? 0 : Convert.ToInt32(reader["canceled"]);
            }
        }

        var byAgent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var agentCmd = connection.CreateCommand();
        agentCmd.CommandText = """
            SELECT agent_id, COUNT(*) AS task_count
            FROM archived_tasks
            CROSS JOIN LATERAL jsonb_array_elements_text(COALESCE(data -> 'agentIds', '[]'::jsonb)) AS agent_id
            GROUP BY agent_id;
            """;

        await using (var reader = await agentCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                byAgent[reader.GetString(0)] = reader.GetInt32(1);
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
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM archived_tasks WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", taskId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count > 0;
    }

    private static ArchivedTask MapToArchived(AgentTask task)
    {
        var history = task.History ?? [];
        var agentIds = history
            .Where(static message => message.Role == Role.Agent)
            .Select(static message => message.Extensions?.FirstOrDefault() ?? "unknown")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var userInput = history
            .Where(static message => message.Role == Role.User)
            .SelectMany(static message => message.Parts?.Where(static part => part.ContentCase == PartContentCase.Text) ?? [])
            .FirstOrDefault()?.Text;

        var finalResponse = history
            .Where(static message => message.Role == Role.Agent)
            .SelectMany(static message => message.Parts?.Where(static part => part.ContentCase == PartContentCase.Text) ?? [])
            .LastOrDefault()?.Text;

        var messages = history.Select(static message => new ArchivedMessage
        {
            Role = message.Role.ToString(),
            Text = string.Join(' ', message.Parts?.Where(static part => part.ContentCase == PartContentCase.Text).Select(static part => part.Text) ?? []),
            MessageId = message.MessageId,
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
            CreatedAt = task.Status.Timestamp?.UtcDateTime ?? DateTime.UtcNow,
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
            parameters["status"] = criteria.Status;
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentId))
        {
            clauses.Add("COALESCE(data -> 'agentIds', '[]'::jsonb) ? @agentId");
            parameters["agentId"] = criteria.AgentId;
        }

        if (criteria.FromDate.HasValue)
        {
            clauses.Add("archived_at >= @fromDate");
            parameters["fromDate"] = criteria.FromDate.Value;
        }

        if (criteria.ToDate.HasValue)
        {
            clauses.Add("archived_at <= @toDate");
            parameters["toDate"] = criteria.ToDate.Value;
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            clauses.Add("data::text ILIKE '%' || @search || '%'");
            parameters["search"] = criteria.Search;
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : string.Empty;
        return (whereClause, parameters);
    }
}
