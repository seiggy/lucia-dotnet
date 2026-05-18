using System.Text.Json;

using lucia.Wyoming.Diarization;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed persistent speaker profile store.
/// </summary>
public sealed class PostgresSpeakerProfileStore : ISpeakerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresSpeakerProfileStore(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM speaker_profiles WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<SpeakerProfile>(json, JsonOptions) : null;
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetAllAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM speaker_profiles;";

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetProvisionalProfilesAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM speaker_profiles WHERE is_provisional = TRUE;";

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM speaker_profiles WHERE is_provisional = FALSE;";

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task CreateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO speaker_profiles (id, is_provisional, last_seen_at, data)
            VALUES (@id, @isProvisional, @lastSeenAt, @data);
            """;
        cmd.Parameters.AddWithValue("id", profile.Id);
        cmd.Parameters.AddWithValue("isProvisional", profile.IsProvisional);
        cmd.Parameters.AddWithValue("lastSeenAt", profile.LastSeenAt.UtcDateTime);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(profile, JsonOptions) });

        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Speaker profile '{profile.Id}' already exists.", ex);
        }
    }

    public async Task UpdateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE speaker_profiles
            SET is_provisional = @isProvisional, last_seen_at = @lastSeenAt, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", profile.Id);
        cmd.Parameters.AddWithValue("isProvisional", profile.IsProvisional);
        cmd.Parameters.AddWithValue("lastSeenAt", profile.LastSeenAt.UtcDateTime);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(profile, JsonOptions) });

        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new KeyNotFoundException($"Speaker profile '{profile.Id}' was not found.");
        }
    }

    public async Task<SpeakerProfile?> UpdateAtomicAsync(string id, Func<SpeakerProfile, SpeakerProfile> transform, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(transform);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var selectCmd = connection.CreateCommand();
            selectCmd.Transaction = transaction;
            selectCmd.CommandText = "SELECT data::text FROM speaker_profiles WHERE id = @id FOR UPDATE;";
            selectCmd.Parameters.AddWithValue("id", id);

            var result = await selectCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is not string json)
            {
                throw new InvalidOperationException($"Profile '{id}' not found");
            }

            var existing = JsonSerializer.Deserialize<SpeakerProfile>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Profile '{id}' not found");
            var updated = transform(existing);

            await using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = """
                UPDATE speaker_profiles
                SET is_provisional = @isProvisional, last_seen_at = @lastSeenAt, data = @data
                WHERE id = @id;
                """;
            updateCmd.Parameters.AddWithValue("id", id);
            updateCmd.Parameters.AddWithValue("isProvisional", updated.IsProvisional);
            updateCmd.Parameters.AddWithValue("lastSeenAt", updated.LastSeenAt.UtcDateTime);
            updateCmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(updated, JsonOptions) });

            await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return updated;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM speaker_profiles WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(int retentionDays, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retentionDays);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT data::text FROM speaker_profiles
            WHERE is_provisional = TRUE AND last_seen_at < @cutoff;
            """;
        cmd.Parameters.AddWithValue("cutoff", cutoff.UtcDateTime);

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SpeakerProfile>> ReadProfilesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<SpeakerProfile>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var profile = JsonSerializer.Deserialize<SpeakerProfile>(reader.GetString(0), JsonOptions);
            if (profile is not null)
            {
                results.Add(profile);
            }
        }

        return results;
    }
}
