using System.Text.Json;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="ITraceRepository"/>.
/// </summary>
public sealed class SqliteTraceRepository : ITraceRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SqliteTraceRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InsertTraceAsync(ConversationTrace trace, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversation_traces (id, session_id, timestamp, user_input, label_status, is_errored, data)
            VALUES (@id, @sessionId, @timestamp, @userInput, @labelStatus, @isErrored, @data);
            """;
        cmd.Parameters.AddWithValue("@id", trace.Id);
        cmd.Parameters.AddWithValue("@sessionId", trace.SessionId);
        cmd.Parameters.AddWithValue("@timestamp", trace.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@userInput", (object?)trace.UserInput ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@labelStatus", trace.Label.Status.ToString());
        cmd.Parameters.AddWithValue("@isErrored", trace.IsErrored ? 1 : 0);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(trace, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ConversationTrace?> GetTraceAsync(string traceId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM conversation_traces WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", traceId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ConversationTrace>(json, JsonOptions) : null;
    }

    public async Task<List<ConversationTrace>> GetTracesBySessionIdAsync(string sessionId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM conversation_traces WHERE session_id = @sessionId ORDER BY timestamp ASC;";
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        return await ReadTracesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<ConversationTrace>> ListTracesAsync(TraceFilterCriteria filter, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var (whereClause, parameters) = BuildTraceFilter(filter);

        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM conversation_traces{whereClause};";
        foreach (var p in parameters)
        {
            countCmd.Parameters.AddWithValue(p.Key, p.Value);
        }

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

        var skip = (filter.Page - 1) * filter.PageSize;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT data FROM conversation_traces{whereClause} ORDER BY timestamp DESC LIMIT @limit OFFSET @offset;";
        foreach (var p in parameters)
        {
            cmd.Parameters.AddWithValue(p.Key, p.Value);
        }
        cmd.Parameters.AddWithValue("@limit", filter.PageSize);
        cmd.Parameters.AddWithValue("@offset", skip);

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
        if (existing is null) return;

        existing.Label = label;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE conversation_traces
            SET label_status = @labelStatus, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", traceId);
        cmd.Parameters.AddWithValue("@labelStatus", label.Status.ToString());
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(existing, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteTraceAsync(string traceId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM conversation_traces WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", traceId);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> DeleteOldUnlabeledAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O");

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM conversation_traces
            WHERE label_status = @status AND timestamp < @cutoff;
            """;
        cmd.Parameters.AddWithValue("@status", LabelStatus.Unlabeled.ToString());
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task InsertExportRecordAsync(DatasetExportRecord export, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dataset_exports (id, data)
            VALUES (@id, @data);
            """;
        cmd.Parameters.AddWithValue("@id", export.Id);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(export, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<DatasetExportRecord?> GetExportRecordAsync(string exportId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM dataset_exports WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", exportId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<DatasetExportRecord>(json, JsonOptions) : null;
    }

    public async Task<List<DatasetExportRecord>> ListExportRecordsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM dataset_exports ORDER BY id DESC;";

        var results = new List<DatasetExportRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var record = JsonSerializer.Deserialize<DatasetExportRecord>(json, JsonOptions);
            if (record is not null)
            {
                results.Add(record);
            }
        }

        return results;
    }

    public async Task<List<ConversationTrace>> GetTracesForExportAsync(ExportFilterCriteria filter, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var (whereClause, parameters) = BuildExportFilter(filter);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT data FROM conversation_traces{whereClause} ORDER BY timestamp DESC;";
        foreach (var p in parameters)
        {
            cmd.Parameters.AddWithValue(p.Key, p.Value);
        }

        return await ReadTracesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<TraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Scalar counts
        using var statsCmd = connection.CreateCommand();
        statsCmd.CommandText = """
            SELECT
                COUNT(*) AS total,
                SUM(CASE WHEN label_status = 'Unlabeled' THEN 1 ELSE 0 END) AS unlabeled,
                SUM(CASE WHEN label_status = 'Positive' THEN 1 ELSE 0 END) AS positive,
                SUM(CASE WHEN label_status = 'Negative' THEN 1 ELSE 0 END) AS negative,
                SUM(CASE WHEN is_errored = 1 THEN 1 ELSE 0 END) AS errored
            FROM conversation_traces;
            """;

        int total = 0, unlabeled = 0, positive = 0, negative = 0, errored = 0;
        using (var reader = await statsCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
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

        // Agent aggregation using SQLite JSON functions
        var byAgent = new Dictionary<string, int>();
        var errorsByAgent = new Dictionary<string, int>();

        using var agentCmd = connection.CreateCommand();
        agentCmd.CommandText = """
            SELECT
                json_extract(je.value, '$.agentId') AS agent_id,
                COUNT(*) AS trace_count,
                SUM(CASE WHEN json_extract(je.value, '$.success') = 0 THEN 1 ELSE 0 END) AS error_count
            FROM conversation_traces ct,
                 json_each(json_extract(ct.data, '$.agentExecutions')) je
            WHERE json_extract(je.value, '$.agentId') IS NOT NULL
            GROUP BY agent_id;
            """;

        using (var reader = await agentCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var agentId = reader.GetString(0);
                var traceCount = reader.GetInt32(1);
                var errorCount = reader.GetInt32(2);
                byAgent[agentId] = traceCount;
                if (errorCount > 0) errorsByAgent[agentId] = errorCount;
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

    private static async Task<List<ConversationTrace>> ReadTracesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<ConversationTrace>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var trace = JsonSerializer.Deserialize<ConversationTrace>(json, JsonOptions);
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
            parameters["@fromDate"] = criteria.FromDate.Value.ToString("O");
        }

        if (criteria.ToDate.HasValue)
        {
            clauses.Add("timestamp <= @toDate");
            parameters["@toDate"] = criteria.ToDate.Value.ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentFilter))
        {
            // Filter by agent in JSON data field
            clauses.Add("data LIKE @agentFilter");
            parameters["@agentFilter"] = $"%\"agentId\":\"{criteria.AgentFilter}\"%";
        }

        if (!string.IsNullOrWhiteSpace(criteria.ModelFilter))
        {
            clauses.Add("data LIKE @modelFilter");
            parameters["@modelFilter"] = $"%\"modelDeploymentName\":\"{criteria.ModelFilter}\"%";
        }

        if (criteria.LabelFilter.HasValue)
        {
            clauses.Add("label_status = @labelStatus");
            parameters["@labelStatus"] = criteria.LabelFilter.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            clauses.Add("user_input LIKE @searchText");
            parameters["@searchText"] = $"%{criteria.SearchText}%";
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        return (whereClause, parameters);
    }

    private static (string WhereClause, Dictionary<string, object> Parameters) BuildExportFilter(ExportFilterCriteria criteria)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (criteria.LabelFilter.HasValue)
        {
            clauses.Add("label_status = @labelStatus");
            parameters["@labelStatus"] = criteria.LabelFilter.Value.ToString();
        }

        if (criteria.FromDate.HasValue)
        {
            clauses.Add("timestamp >= @fromDate");
            parameters["@fromDate"] = criteria.FromDate.Value.ToString("O");
        }

        if (criteria.ToDate.HasValue)
        {
            clauses.Add("timestamp <= @toDate");
            parameters["@toDate"] = criteria.ToDate.Value.ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(criteria.AgentFilter))
        {
            clauses.Add("data LIKE @agentFilter");
            parameters["@agentFilter"] = $"%\"agentId\":\"{criteria.AgentFilter}\"%";
        }

        if (!string.IsNullOrWhiteSpace(criteria.ModelFilter))
        {
            clauses.Add("data LIKE @modelFilter");
            parameters["@modelFilter"] = $"%\"modelDeploymentName\":\"{criteria.ModelFilter}\"%";
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        return (whereClause, parameters);
    }
}
