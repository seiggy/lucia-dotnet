using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed LLM usage ledger.
/// </summary>
public sealed class PostgresLlmUsageStore : ILlmUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresLlmUsageStore(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordAsync(LlmUsageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO llm_usage (id, member_id, timestamp, cost, input_tokens, output_tokens, data)
            VALUES (@id, @memberId, @timestamp, @cost, @inputTokens, @outputTokens, @data);
            """;
        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("memberId", record.MemberId);
        cmd.Parameters.AddWithValue("timestamp", record.Timestamp.ToUniversalTime());
        cmd.Parameters.AddWithValue("cost", record.CostUsd);
        cmd.Parameters.AddWithValue("inputTokens", record.InputTokens);
        cmd.Parameters.AddWithValue("outputTokens", record.OutputTokens);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(record, JsonOptions)
        });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LlmUsageTotals> GetTotalsAsync(
        string memberId,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(input_tokens), 0), COALESCE(SUM(output_tokens), 0), COALESCE(SUM(cost), 0)
            FROM llm_usage
            WHERE member_id = @memberId AND timestamp >= @since;
            """;
        cmd.Parameters.AddWithValue("memberId", memberId);
        cmd.Parameters.AddWithValue("since", since.ToUniversalTime());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return LlmUsageTotals.Empty;
        }

        var calls = reader.GetInt64(0);
        if (calls == 0)
        {
            return LlmUsageTotals.Empty;
        }

        return new LlmUsageTotals(
            (int)calls,
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetDecimal(3));
    }

    public async Task<IReadOnlyList<LlmUsageRecord>> ListRecentAsync(
        string memberId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM llm_usage WHERE member_id = @memberId ORDER BY timestamp DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("memberId", memberId);
        cmd.Parameters.AddWithValue("limit", limit);

        var results = new List<LlmUsageRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = JsonSerializer.Deserialize<LlmUsageRecord>(reader.GetString(0), JsonOptions);
            if (record is not null)
            {
                results.Add(record);
            }
        }

        return results;
    }
}
