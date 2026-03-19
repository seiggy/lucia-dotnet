using System.Text.Json;
using lucia.Wyoming.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed persistent model preference store.
/// </summary>
public sealed class SqliteModelPreferenceStore : IModelPreferenceStore
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteModelPreferenceStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SqliteModelPreferenceStore(SqliteConnectionFactory connectionFactory, ILogger<SqliteModelPreferenceStore> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, data FROM model_preferences;";

            var result = new Dictionary<string, string>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                var json = reader.GetString(1);
                var pref = JsonSerializer.Deserialize<ActiveModelPreference>(json, JsonOptions);
                if (pref is not null)
                {
                    result[key] = pref.ModelId;
                }
            }

            _logger.LogInformation("Loaded {Count} persisted model preference(s)", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load model preferences from SQLite \u2014 starting with defaults");
            return [];
        }
    }

    public async Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        try
        {
            var pref = new ActiveModelPreference
            {
                EngineType = key,
                ModelId = value,
                UpdatedAt = DateTime.UtcNow,
            };

            using var connection = _connectionFactory.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO model_preferences (id, data)
                VALUES (@id, @data)
                ON CONFLICT(id) DO UPDATE SET data = @data;
                """;
            cmd.Parameters.AddWithValue("@id", key);
            cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(pref, JsonOptions));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist model preference for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM model_preferences WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", key);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove model preference for {Key}", key);
        }
    }
}
