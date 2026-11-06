using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed action approval ledger.
/// </summary>
public sealed class PostgresActionRecordStore : IActionRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresActionRecordStore(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(ActionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO action_records (id, requester_id, status, created_at, data)
            VALUES (@id, @requesterId, @status, @createdAt, @data);
            """;
        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("requesterId", record.RequesterId);
        cmd.Parameters.AddWithValue("status", record.Status.ToString());
        cmd.Parameters.AddWithValue("createdAt", record.CreatedAt.ToUniversalTime());
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(record, JsonOptions)
        });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActionRecord?> GetAsync(string actionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM action_records WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", actionId);

        var json = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return json is string value ? JsonSerializer.Deserialize<ActionRecord>(value, JsonOptions) : null;
    }

    public async Task<bool> TryUpdateAsync(
        ActionRecord updated,
        ActionStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE action_records
            SET status = @status,
                data = @data
            WHERE id = @id AND status = @expectedStatus;
            """;
        cmd.Parameters.AddWithValue("status", updated.Status.ToString());
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(updated, JsonOptions)
        });
        cmd.Parameters.AddWithValue("id", updated.Id);
        cmd.Parameters.AddWithValue("expectedStatus", expectedStatus.ToString());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<IReadOnlyList<ActionRecord>> ListAwaitingDecisionAsync(
        string approverId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approverId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM action_records WHERE status = @status ORDER BY created_at ASC;";
        cmd.Parameters.AddWithValue("status", ActionStatus.AwaitingDecision.ToString());

        var results = new List<ActionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = JsonSerializer.Deserialize<ActionRecord>(reader.GetString(0), JsonOptions);
            if (record is not null && record.ApproverIds.Contains(approverId, StringComparer.Ordinal))
            {
                results.Add(record);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ActionRecord>> ListForRequesterAsync(
        string requesterId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM action_records WHERE requester_id = @requesterId ORDER BY created_at DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("requesterId", requesterId);
        cmd.Parameters.AddWithValue("limit", limit);

        var results = new List<ActionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var record = JsonSerializer.Deserialize<ActionRecord>(reader.GetString(0), JsonOptions);
            if (record is not null)
            {
                results.Add(record);
            }
        }

        return results;
    }
}
