using System.Text.Json;
using lucia.Wyoming.Telemetry;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed persistent transcript store.
/// </summary>
public sealed class SqliteTranscriptStore : ITranscriptStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SqliteTranscriptStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(TranscriptRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO voice_transcripts (id, session_id, timestamp, speaker_id, data)
            VALUES (@id, @sessionId, @timestamp, @speakerId, @data);
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@sessionId", record.SessionId);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("@speakerId", (object?)record.SpeakerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(record, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<TranscriptRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM voice_transcripts WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<TranscriptRecord>(json, JsonOptions) : null;
    }

    public async Task<IReadOnlyList<TranscriptRecord>> QueryAsync(
        string? sessionId = null,
        DateTimeOffset? since = null,
        string? speakerId = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var clauses = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            clauses.Add("session_id = @sessionId");
            parameters["@sessionId"] = sessionId;
        }

        if (since.HasValue)
        {
            clauses.Add("timestamp >= @since");
            parameters["@since"] = since.Value.ToString("O");
        }

        if (!string.IsNullOrWhiteSpace(speakerId))
        {
            clauses.Add("speaker_id = @speakerId");
            parameters["@speakerId"] = speakerId;
        }

        var whereClause = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT data FROM voice_transcripts{whereClause} ORDER BY timestamp DESC LIMIT @limit;";
        foreach (var p in parameters)
        {
            cmd.Parameters.AddWithValue(p.Key, p.Value);
        }
        cmd.Parameters.AddWithValue("@limit", limit);

        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptRecord>> GetRecentAsync(int limit = 20, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM voice_transcripts ORDER BY timestamp DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        return await ReadRecordsAsync(cmd, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<TranscriptRecord>> ReadRecordsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var results = new List<TranscriptRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var json = reader.GetString(0);
            var record = JsonSerializer.Deserialize<TranscriptRecord>(json, JsonOptions);
            if (record is not null)
            {
                results.Add(record);
            }
        }

        return results;
    }
}
