using System.Text.Json;

using lucia.Wyoming.Models;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed persistent model preference store.
/// </summary>
public sealed partial class PostgresModelPreferenceStore : IModelPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresModelPreferenceStore> _logger;

    public PostgresModelPreferenceStore(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory,
        ILogger<PostgresModelPreferenceStore> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> LoadAllAsync(CancellationToken ct = default)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, data::text FROM model_preferences;";

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var key = reader.GetString(0);
                var pref = JsonSerializer.Deserialize<ActiveModelPreference>(reader.GetString(1), JsonOptions);
                if (pref is not null)
                {
                    result[key] = pref.ModelId;
                }
            }

            LogLoadedPreferences(_logger, result.Count);
            return result;
        }
        catch (Exception ex)
        {
            LogLoadFailed(_logger, ex);
            return [];
        }
    }

    public async Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        try
        {
            var preference = new ActiveModelPreference
            {
                EngineType = key,
                ModelId = value,
                UpdatedAt = DateTime.UtcNow,
            };

            await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO model_preferences (id, data)
                VALUES (@id, @data)
                ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data;
                """;
            cmd.Parameters.AddWithValue("id", key);
            cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(preference, JsonOptions) });

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSaveFailed(_logger, ex, key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM model_preferences WHERE id = @id;";
            cmd.Parameters.AddWithValue("id", key);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogRemoveFailed(_logger, ex, key);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Loaded {Count} persisted model preference(s)")]
    private static partial void LogLoadedPreferences(ILogger logger, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to load model preferences from PostgreSQL \u2014 starting with defaults")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Failed to persist model preference for key {Key}")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string key);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Failed to remove model preference for {Key}")]
    private static partial void LogRemoveFailed(ILogger logger, Exception exception, string key);
}
