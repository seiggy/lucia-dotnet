using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.CommandTracing;
using lucia.Agents.Training.Models;

using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="ICommandTraceRepository"/>.
/// Stores the full trace as a JSON blob with denormalized indexed columns for fast filtering.
/// </summary>
public sealed class SqliteCommandTraceRepository : ICommandTraceRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public SqliteCommandTraceRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(CommandTrace trace, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO command_traces (id, timestamp, clean_text, outcome, skill_id, confidence, total_duration_ms, data)
            VALUES (@id, @timestamp, @cleanText, @outcome, @skillId, @confidence, @totalDurationMs, @data);
            """;
        cmd.Parameters.AddWithValue("@id", trace.Id);
        cmd.Parameters.AddWithValue("@timestamp", trace.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@cleanText", trace.CleanText);
        cmd.Parameters.AddWithValue("@outcome", trace.Outcome.ToString());
        cmd.Parameters.AddWithValue("@skillId", (object?)trace.Match.SkillId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence", trace.Match.Confidence);
        cmd.Parameters.AddWithValue("@totalDurationMs", trace.TotalDurationMs);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(trace, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<CommandTrace?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM command_traces WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<CommandTrace>(json, JsonOptions) : null;
    }

    public async Task<PagedResult<CommandTrace>> ListAsync(CommandTraceFilter filter, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var (whereClause, parameters) = BuildFilter(filter);

        // Count
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM command_traces{whereClause};";
        foreach (var (key, value) in parameters)
            countCmd.Parameters.AddWithValue(key, value);

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

        // Page
        var pageSize = Math.Max(1, filter.PageSize);
        var page = Math.Max(1, filter.Page);
        var offset = (page - 1) * pageSize;

        using var queryCmd = connection.CreateCommand();
        queryCmd.CommandText = $"SELECT data FROM command_traces{whereClause} ORDER BY timestamp DESC LIMIT @limit OFFSET @offset;";
        foreach (var (key, value) in parameters)
            queryCmd.Parameters.AddWithValue(key, value);
        queryCmd.Parameters.AddWithValue("@limit", pageSize);
        queryCmd.Parameters.AddWithValue("@offset", offset);

        var items = new List<CommandTrace>();
        using var reader = await queryCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var trace = JsonSerializer.Deserialize<CommandTrace>(json, JsonOptions);
            if (trace is not null)
                items.Add(trace);
        }

        return new PagedResult<CommandTrace>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<CommandTraceStats> GetStatsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*) as total,
                SUM(CASE WHEN outcome = 'CommandHandled' THEN 1 ELSE 0 END) as command_handled,
                SUM(CASE WHEN outcome IN ('LlmFallback', 'LlmCompleted') THEN 1 ELSE 0 END) as llm_fallback,
                SUM(CASE WHEN outcome = 'Error' THEN 1 ELSE 0 END) as errors,
                AVG(total_duration_ms) as avg_duration
            FROM command_traces;
            """;

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        long total = 0, commandHandled = 0, llmFallback = 0, errors = 0;
        double avgDuration = 0;

        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            total = reader.GetInt64(0);
            commandHandled = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
            llmFallback = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            errors = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
            avgDuration = reader.IsDBNull(4) ? 0 : Math.Round(reader.GetDouble(4), 2);
        }

        // Per-skill breakdown
        using var skillCmd = connection.CreateCommand();
        skillCmd.CommandText = """
            SELECT skill_id, COUNT(*) as cnt
            FROM command_traces
            WHERE skill_id IS NOT NULL
            GROUP BY skill_id;
            """;

        var bySkill = new Dictionary<string, long>();
        using var skillReader = await skillCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await skillReader.ReadAsync(ct).ConfigureAwait(false))
        {
            bySkill[skillReader.GetString(0)] = skillReader.GetInt64(1);
        }

        return new CommandTraceStats
        {
            TotalCount = total,
            CommandHandledCount = commandHandled,
            LlmFallbackCount = llmFallback,
            ErrorCount = errors,
            AvgDurationMs = avgDuration,
            BySkill = bySkill,
        };
    }

    private static (string WhereClause, Dictionary<string, object> Parameters) BuildFilter(CommandTraceFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            clauses.Add("clean_text LIKE @search");
            parameters["@search"] = $"%{filter.Search}%";
        }

        if (filter.Outcome is not null)
        {
            clauses.Add("outcome = @outcome");
            parameters["@outcome"] = filter.Outcome.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(filter.SkillId))
        {
            clauses.Add("skill_id = @skillId");
            parameters["@skillId"] = filter.SkillId;
        }

        if (filter.FromDate is not null)
        {
            clauses.Add("timestamp >= @fromDate");
            parameters["@fromDate"] = filter.FromDate.Value.ToString("O");
        }

        if (filter.ToDate is not null)
        {
            clauses.Add("timestamp <= @toDate");
            parameters["@toDate"] = filter.ToDate.Value.AddDays(1).ToString("O");
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        return (whereClause, parameters);
    }
}
