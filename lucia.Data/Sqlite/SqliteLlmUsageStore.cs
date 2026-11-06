using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed LLM usage ledger.
/// </summary>
public sealed class SqliteLlmUsageStore : ILlmUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteLlmUsageStore(
        [FromKeyedServices(SqliteDbNames.Config)] SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordAsync(LlmUsageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO llm_usage (id, member_id, timestamp, cost, input_tokens, output_tokens, data)
            VALUES (@id, @memberId, @timestamp, @cost, @inputTokens, @outputTokens, @data);
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@memberId", record.MemberId);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@cost", record.CostUsd.ToString("0.###############################", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@inputTokens", record.InputTokens);
        cmd.Parameters.AddWithValue("@outputTokens", record.OutputTokens);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(record, JsonOptions));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LlmUsageTotals> GetTotalsAsync(
        string memberId,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT cost, input_tokens, output_tokens FROM llm_usage WHERE member_id = @memberId AND timestamp >= @since;";
        cmd.Parameters.AddWithValue("@memberId", memberId);
        cmd.Parameters.AddWithValue("@since", since.ToUniversalTime().ToString("O"));

        var totalCost = decimal.Zero;
        var totalInput = 0L;
        var totalOutput = 0L;
        var count = 0;

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            count++;
            totalCost += decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            totalInput += reader.GetInt64(1);
            totalOutput += reader.GetInt64(2);
        }

        if (count == 0)
        {
            return LlmUsageTotals.Empty;
        }

        return new LlmUsageTotals(count, totalInput, totalOutput, totalCost);
    }

    public async Task<IReadOnlyList<LlmUsageRecord>> ListRecentAsync(
        string memberId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM llm_usage WHERE member_id = @memberId ORDER BY timestamp DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@memberId", memberId);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<LlmUsageRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
