using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed repository for model provider configurations.
/// Stores the full <see cref="ModelProvider"/> as JSON in the <c>data</c> column.
/// </summary>
public sealed partial class PostgresModelProviderRepository : IModelProviderRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ILogger<PostgresModelProviderRepository> _logger;

    public PostgresModelProviderRepository(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory,
        ILogger<PostgresModelProviderRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<ModelProvider>> GetAllProvidersAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM model_providers ORDER BY name;";

        var providers = new List<ModelProvider>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var provider = JsonSerializer.Deserialize<ModelProvider>(reader.GetString(0), JsonOptions);
            if (provider is not null)
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    public async Task<List<ModelProvider>> GetEnabledProvidersAsync(CancellationToken ct = default)
    {
        var all = await GetAllProvidersAsync(ct).ConfigureAwait(false);
        return all.Where(static provider => provider.Enabled).ToList();
    }

    public async Task<ModelProvider?> GetProviderAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM model_providers WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ModelProvider>(json, JsonOptions) : null;
    }

    public async Task UpsertProviderAsync(ModelProvider provider, CancellationToken ct = default)
    {
        provider.UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(provider, JsonOptions);

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO model_providers (id, name, data)
            VALUES (@id, @name, @data)
            ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", provider.Id);
        cmd.Parameters.AddWithValue("name", provider.Name);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = json });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        LogUpsertedProvider(_logger, provider.Id, provider.Name);
    }

    public async Task DeleteProviderAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM model_providers WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        LogDeletedProvider(_logger, id);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Upserted model provider {ProviderId} ({ProviderName})")]
    private static partial void LogUpsertedProvider(ILogger logger, string providerId, string providerName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Deleted model provider {ProviderId}")]
    private static partial void LogDeletedProvider(ILogger logger, string providerId);
}
