using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed device pairing redemption store.
/// </summary>
public sealed class PostgresDevicePairingStore : IDevicePairingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresDevicePairingStore(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(DevicePairing pairing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairing);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO device_pairings (id, member_id, code_hash, expires_at, redeemed_at, data)
            VALUES (@id, @memberId, @codeHash, @expiresAt, @redeemedAt, @data);
            """;
        cmd.Parameters.AddWithValue("id", pairing.Id);
        cmd.Parameters.AddWithValue("memberId", pairing.MemberId);
        cmd.Parameters.AddWithValue("codeHash", pairing.CodeHash);
        cmd.Parameters.AddWithValue("expiresAt", pairing.ExpiresAt.ToUniversalTime());
        cmd.Parameters.AddWithValue("redeemedAt", pairing.RedeemedAt.HasValue ? pairing.RedeemedAt.Value.ToUniversalTime() : DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(pairing, JsonOptions)
        });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DevicePairing>> ListForMemberAsync(
        string memberId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM device_pairings WHERE member_id = @memberId ORDER BY expires_at DESC;";
        cmd.Parameters.AddWithValue("memberId", memberId);

        var results = new List<DevicePairing>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pairing = JsonSerializer.Deserialize<DevicePairing>(reader.GetString(0), JsonOptions);
            if (pairing is not null)
            {
                results.Add(pairing);
            }
        }

        return results;
    }

    public async Task<DevicePairing?> TryRedeemAsync(
        string codeHash,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);

        var redeemedAt = now.ToUniversalTime();
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE device_pairings
            SET redeemed_at = @redeemedAt,
                data = jsonb_set(data, '{redeemedAt}', @redeemedAtJson::jsonb, true)
            WHERE code_hash = @codeHash
              AND redeemed_at IS NULL
              AND expires_at > @now
            RETURNING data::text;
            """;
        cmd.Parameters.AddWithValue("codeHash", codeHash);
        cmd.Parameters.AddWithValue("redeemedAt", redeemedAt);
        cmd.Parameters.AddWithValue("redeemedAtJson", JsonSerializer.Serialize(redeemedAt, JsonOptions));
        cmd.Parameters.AddWithValue("now", now.ToUniversalTime());

        var json = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return json is string value ? JsonSerializer.Deserialize<DevicePairing>(value, JsonOptions) : null;
    }

    public async Task SetApiKeyAsync(string pairingId, string apiKeyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKeyId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT data::text FROM device_pairings WHERE id = @id;";
        select.Parameters.AddWithValue("id", pairingId);

        var json = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (json is not string value)
        {
            return;
        }

        var pairing = JsonSerializer.Deserialize<DevicePairing>(value, JsonOptions);
        if (pairing is null)
        {
            return;
        }

        pairing.ApiKeyId = apiKeyId;

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE device_pairings SET data = @data WHERE id = @id;";
        update.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(pairing, JsonOptions)
        });
        update.Parameters.AddWithValue("id", pairingId);

        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string pairingId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM device_pairings WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", pairingId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }
}
