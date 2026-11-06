using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed device pairing redemption store.
/// </summary>
public sealed class SqliteDevicePairingStore : IDevicePairingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteDevicePairingStore(
        [FromKeyedServices(SqliteDbNames.Config)] SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(DevicePairing pairing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairing);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO device_pairings (id, member_id, code_hash, expires_at, redeemed_at, data)
            VALUES (@id, @memberId, @codeHash, @expiresAt, @redeemedAt, @data);
            """;
        cmd.Parameters.AddWithValue("@id", pairing.Id);
        cmd.Parameters.AddWithValue("@memberId", pairing.MemberId);
        cmd.Parameters.AddWithValue("@codeHash", pairing.CodeHash);
        cmd.Parameters.AddWithValue("@expiresAt", pairing.ExpiresAt.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@redeemedAt", pairing.RedeemedAt.HasValue ? pairing.RedeemedAt.Value.ToUniversalTime().ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(pairing, JsonOptions));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DevicePairing>> ListForMemberAsync(
        string memberId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM device_pairings WHERE member_id = @memberId ORDER BY expires_at DESC;";
        cmd.Parameters.AddWithValue("@memberId", memberId);

        var results = new List<DevicePairing>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
        using var connection = _connectionFactory.CreateConnection();

        using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = "SELECT data FROM device_pairings WHERE code_hash = @codeHash AND redeemed_at IS NULL AND expires_at > @now LIMIT 1;";
            selectCmd.Parameters.AddWithValue("@codeHash", codeHash);
            selectCmd.Parameters.AddWithValue("@now", redeemedAt.ToString("O"));

            var json = await selectCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (json is not string rowJson)
            {
                return null;
            }

            var existing = JsonSerializer.Deserialize<DevicePairing>(rowJson, JsonOptions);
            if (existing is null)
            {
                return null;
            }

            existing.RedeemedAt = redeemedAt;

            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE device_pairings
                SET redeemed_at = @redeemedAt, data = @data
                WHERE code_hash = @codeHash AND redeemed_at IS NULL AND expires_at > @now;
                """;
            update.Parameters.AddWithValue("@redeemedAt", redeemedAt.ToString("O"));
            update.Parameters.AddWithValue("@data", JsonSerializer.Serialize(existing, JsonOptions));
            update.Parameters.AddWithValue("@codeHash", codeHash);
            update.Parameters.AddWithValue("@now", redeemedAt.ToString("O"));

            var rows = await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (rows != 1)
            {
                return null;
            }

            return existing;
        }
    }

    public async Task SetApiKeyAsync(string pairingId, string apiKeyId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKeyId);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM device_pairings WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", pairingId);

        var json = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (json is null)
        {
            return;
        }

        var pairing = JsonSerializer.Deserialize<DevicePairing>(json, JsonOptions);
        if (pairing is null)
        {
            return;
        }

        pairing.ApiKeyId = apiKeyId;

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE device_pairings SET data = @data WHERE id = @id;";
        update.Parameters.AddWithValue("@data", JsonSerializer.Serialize(pairing, JsonOptions));
        update.Parameters.AddWithValue("@id", pairingId);
        await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string pairingId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingId);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM device_pairings WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", pairingId);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }
}
