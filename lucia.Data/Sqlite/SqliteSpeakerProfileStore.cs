using System.Text.Json;
using lucia.Wyoming.Diarization;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed persistent speaker profile store.
/// </summary>
public sealed class SqliteSpeakerProfileStore : ISpeakerProfileStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SqliteSpeakerProfileStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM speaker_profiles WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<SpeakerProfile>(json, JsonOptions) : null;
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetAllAsync(CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM speaker_profiles;";

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetProvisionalProfilesAsync(CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM speaker_profiles WHERE is_provisional = 1;";

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM speaker_profiles WHERE is_provisional = 0;";

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task CreateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO speaker_profiles (id, is_provisional, last_seen_at, data)
            VALUES (@id, @isProvisional, @lastSeenAt, @data);
            """;
        cmd.Parameters.AddWithValue("@id", profile.Id);
        cmd.Parameters.AddWithValue("@isProvisional", profile.IsProvisional ? 1 : 0);
        cmd.Parameters.AddWithValue("@lastSeenAt", profile.LastSeenAt.ToString("O"));
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(profile, JsonOptions));

        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            throw new InvalidOperationException($"Speaker profile '{profile.Id}' already exists.", ex);
        }
    }

    public async Task UpdateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE speaker_profiles
            SET is_provisional = @isProvisional, last_seen_at = @lastSeenAt, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", profile.Id);
        cmd.Parameters.AddWithValue("@isProvisional", profile.IsProvisional ? 1 : 0);
        cmd.Parameters.AddWithValue("@lastSeenAt", profile.LastSeenAt.ToString("O"));
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(profile, JsonOptions));

        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new KeyNotFoundException($"Speaker profile '{profile.Id}' was not found.");
        }
    }

    public async Task<SpeakerProfile?> UpdateAtomicAsync(
        string id,
        Func<SpeakerProfile, SpeakerProfile> transform,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(transform);

        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            using var selectCmd = connection.CreateCommand();
            selectCmd.Transaction = transaction;
            selectCmd.CommandText = "SELECT data FROM speaker_profiles WHERE id = @id;";
            selectCmd.Parameters.AddWithValue("@id", id);

            var result = await selectCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is not string json)
            {
                throw new InvalidOperationException($"Profile '{id}' not found");
            }

            var existing = JsonSerializer.Deserialize<SpeakerProfile>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Profile '{id}' not found");

            var updated = transform(existing);

            using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = """
                UPDATE speaker_profiles
                SET is_provisional = @isProvisional, last_seen_at = @lastSeenAt, data = @data
                WHERE id = @id;
                """;
            updateCmd.Parameters.AddWithValue("@id", id);
            updateCmd.Parameters.AddWithValue("@isProvisional", updated.IsProvisional ? 1 : 0);
            updateCmd.Parameters.AddWithValue("@lastSeenAt", updated.LastSeenAt.ToString("O"));
            updateCmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(updated, JsonOptions));

            await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            transaction.Commit();

            return updated;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM speaker_profiles WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(
        int retentionDays,
        CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retentionDays);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O");

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT data FROM speaker_profiles
            WHERE is_provisional = 1 AND last_seen_at < @cutoff;
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        return await ReadProfilesAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SpeakerProfile>> ReadProfilesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<SpeakerProfile>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var profile = JsonSerializer.Deserialize<SpeakerProfile>(json, JsonOptions);
            if (profile is not null)
            {
                results.Add(profile);
            }
        }

        return results;
    }
}
