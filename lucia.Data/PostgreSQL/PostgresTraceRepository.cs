using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Training;
using lucia.Agents.Training.Models;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="ITraceRepository"/>.
/// </summary>
public sealed class PostgresTraceRepository : ITraceRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresTraceRepository([FromKeyedServices(PostgresDbNames.Traces)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertTraceAsync(ConversationTrace trace, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversation_traces (id, session_id, timestamp, user_input, label_status, is_errored, data)
            VALUES (@id, @sessionId, @timestamp, @userInput, @labelStatus, @isErrored, @data);
            """;
        cmd.Parameters.AddWithValue("id", trace.Id);
        cmd.Parameters.AddWithValue("sessionId", (object?)trace.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timestamp", trace.Timestamp);
        cmd.Parameters.AddWithValue("userInput", (object?)trace.UserInput ?? DBNull.Value);
        cmd.Parameters.AddWithValue("labelStatus", trace.Label.Status.ToString());
        cmd.Parameters.AddWithValue("isErrored", trace.IsErrored);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(trace, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ConversationTrace?> GetTraceAsync(string traceId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM conversation_traces WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", traceId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ConversationTrace>(json, JsonOptions) : null;
    }

    public async Task<List<ConversationTrace>> GetTracesBySessionIdAsync(string sessionId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM conversation_traces WHERE session_id = @sessionId ORDER BY timestamp ASC;";
        cmd.Parameters.AddWithValue("sessionId", sessionId);

        return await ReadTracesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<ConversationTrace>> ListTracesAsync(TraceFilterCriteria filter, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);

        var (whereClause, parameters) = BuildTraceFilter(filter);

        await using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM conversation_traces{whereClause};";
        foreach (var parameter in parameters)
        {
            countCmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

        var skip = (filter.Page - 1) * filter.PageSize;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT data::text FROM conversation_traces{whereClause} ORDER BY timestamp DESC LIMIT @limit OFFSET @offset;";
        foreach (var parameter in parameters)
        {
            cmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }
        cmd.Parameters.AddWithValue("limit", filter.PageSize);
        cmd.Parameters.AddWithValue("offset", skip);

        var items = await ReadTracesAsync(cmd, ct).ConfigureAwait(false);

        return new PagedResult<ConversationTrace>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public async Task UpdateLabelAsync(string traceId, TraceLabel label, CancellationToken ct = default)
    {
        var existing = await GetTraceAsync(traceId, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        existing.Label = label;

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE conversation_traces
            SET label_status = @labelStatus, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", traceId);
        cmd.Parameters.AddWithValue("labelStatus", label.Status.ToString());
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(existing, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteTraceAsync(string traceId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM conversation_traces WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", traceId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteOldUnlabeledAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM conversation_traces
            WHERE label_status = @status AND timestamp < @cutoff;
            """;
        cmd.Parameters.AddWithValue("status", LabelStatus.Unlabeled.ToString());
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertExportRecordAsync(DatasetExportRecord export, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dataset_exports (id, data)
            VALUES (@id, @data);
            """;
        cmd.Parameters.AddWithValue("id", export.Id);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(export, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<DatasetExportRecord?> GetExportRecordAsync(string exportId, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM dataset_exports WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", exportId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<DatasetExportRecord>(json, JsonOptions) : null;
    }

    public async Task<List<DatasetExportRecord>> ListExportRecordsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM dataset_exports ORDER BY id DESC;";

        var results = new List<DatasetExportRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var record = JsonSerializer.Deserialize<DatasetExportRecord>(reader.GetString(0), JsonOptions);
            if (record is not null)
            {
                results.Add(record);
            }
        }

        return results;
    }

    public async Task<List<ConversationTrace>> GetTracesForExportAsync(ExportFilterCriteria filter, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);

        var (whereClause, parameters) = BuildExportFilter(filter);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT data::text FROM conversation_traces{whereClause} ORDER BY timestamp DESC;";
        foreach (var parameter in parameters)
        {
            cmd.Parameters.AddWithValue(parameter.Key, parameter.Value);
        }

        return await ReadTracesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<TraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);

        await using var statsCmd = connection.CreateCommand();
        statsCmd.CommandText = """
            SELECT
                COUNT(*) AS total,
                SUM(CASE WHEN label_status = 'Unlabeled' THEN 1 ELSE 0 END) AS unlabeled,
                SUM(CASE WHEN label_status = 'Positive' THEN 1 ELSE 0 END) AS positive,
                SUM(CASE WHEN label_status = 'Negative' THEN 1 ELSE 0 END) AS negative,
                SUM(CASE WHEN is_errored THEN 1 ELSE 0 END) AS errored
            FROM conversation_traces;
            """;

        var total = 0;
        var unlabeled = 0;
        var positive = 0;
        var negative = 0;
        var errored = 0;
        await using (var reader = await statsCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                total = reader["total"] is DBNull ? 0 : Convert.ToInt32(reader["total"]);
                unlabeled = reader["unlabeled"] is DBNull ? 0 : Convert.ToInt32(reader["unlabeled"]);
                positive = reader["positive"] is DBNull ? 0 : Convert.ToInt32(reader["positive"]);
                negative = reader["negative"] is DBNull ? 0 : Convert.ToInt32(reader["negative"]);
                errored = reader["errored"] is DBNull ? 0 : Convert.ToInt32(reader["errored"]);
            }
        }

        var byAgent = new Dictionary<string, int>();
        var errorsByAgent = new Dictionary<string, int>();

        await using var agentCmd = connection.CreateCommand();
        agentCmd.CommandText = """
            SELECT
                execution ->> 'agentId' AS agent_id,
                COUNT(*) AS trace_count,
                SUM(CASE WHEN execution ? 'success' AND (execution ->> 'success')::boolean = FALSE THEN 1 ELSE 0 END) AS error_count
            FROM conversation_traces
            CROSS JOIN LATERAL jsonb_array_elements(COALESCE(data -> 'agentExecutions', '[]'::jsonb)) AS execution
            WHERE execution ->> 'agentId' IS NOT NULL
            GROUP BY agent_id;
            """;

        await using (var reader = await agentCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var agentId = reader.GetString(0);
                var traceCount = reader.GetInt32(1);
                var errorCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                byAgent[agentId] = traceCount;
                if (errorCount > 0)
                {
                    errorsByAgent[agentId] = errorCount;
                }
            }
        }

        return new TraceStats
        {
            TotalTraces = total,
            UnlabeledCount = unlabeled,
            PositiveCount = positive,
            NegativeCount = negative,
            ErroredCount = errored,
            ByAgent = byAgent,
            ErrorsByAgent = errorsByAgent,
        };
    }

    private static async Task<List<ConversationTrace>> ReadTracesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<ConversationTrace>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var trace = JsonSerializer.Deserialize<ConversationTrace>(reader.GetString(0), JsonOptions);
            if (trace is not null)
            {
                results.Add(trace);
            }
        }

        return results;
    }

    private static (string WhereClause, Dictionary<string, object> Parameters) BuildTraceFilter(TraceFilterCriteria criteria)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (criteria.FromDate.HasValue)
        {
            clauses.Add("timestamp >= @fromDate");
            parameters["fromDate"] = criteria.FromDate.Value;
        }

        if (criteria.ToDate.HasValue)
        {
            clauses.Add("timestamp <= @toDate");
            parameters["toDate"] = criteria.ToDate.Value;
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentFilter))
        {
            clauses.Add("EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE(data -> 'agentExecutions', '[]'::jsonb)) AS execution WHERE execution ->> 'agentId' = @agentFilter)");
            parameters["agentFilter"] = criteria.AgentFilter;
        }

        if (!string.IsNullOrWhiteSpace(criteria.ModelFilter))
        {
            clauses.Add("EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE(data -> 'agentExecutions', '[]'::jsonb)) AS execution WHERE execution ->> 'modelDeploymentName' = @modelFilter)");
            parameters["modelFilter"] = criteria.ModelFilter;
        }

        if (criteria.LabelFilter.HasValue)
        {
            clauses.Add("label_status = @labelStatus");
            parameters["labelStatus"] = criteria.LabelFilter.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            clauses.Add("user_input ILIKE '%' || @searchText || '%'");
            parameters["searchText"] = criteria.SearchText;
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : string.Empty;
        return (whereClause, parameters);
    }

    private static (string WhereClause, Dictionary<string, object> Parameters) BuildExportFilter(ExportFilterCriteria criteria)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (criteria.LabelFilter.HasValue)
        {
            clauses.Add("label_status = @labelStatus");
            parameters["labelStatus"] = criteria.LabelFilter.Value.ToString();
        }

        if (criteria.FromDate.HasValue)
        {
            clauses.Add("timestamp >= @fromDate");
            parameters["fromDate"] = criteria.FromDate.Value;
        }

        if (criteria.ToDate.HasValue)
        {
            clauses.Add("timestamp <= @toDate");
            parameters["toDate"] = criteria.ToDate.Value;
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentFilter))
        {
            clauses.Add("EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE(data -> 'agentExecutions', '[]'::jsonb)) AS execution WHERE execution ->> 'agentId' = @agentFilter)");
            parameters["agentFilter"] = criteria.AgentFilter;
        }

        if (!string.IsNullOrWhiteSpace(criteria.ModelFilter))
        {
            clauses.Add("EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE(data -> 'agentExecutions', '[]'::jsonb)) AS execution WHERE execution ->> 'modelDeploymentName' = @modelFilter)");
            parameters["modelFilter"] = criteria.ModelFilter;
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : string.Empty;
        return (whereClause, parameters);
    }
}
