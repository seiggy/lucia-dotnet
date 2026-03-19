using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed repository for model provider configurations.
/// Stores the full <see cref="ModelProvider"/> as JSON in the <c>data</c> column.
/// </summary>
public sealed class SqliteModelProviderRepository : IModelProviderRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteModelProviderRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public SqliteModelProviderRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<SqliteModelProviderRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<ModelProvider>> GetAllProvidersAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM model_providers ORDER BY name;";

        var providers = new List<ModelProvider>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var provider = JsonSerializer.Deserialize<ModelProvider>(reader.GetString(0), JsonOptions);
            if (provider is not null)
                providers.Add(provider);
        }

        return providers;
    }

    public async Task<List<ModelProvider>> GetEnabledProvidersAsync(CancellationToken ct = default)
    {
        var all = await GetAllProvidersAsync(ct).ConfigureAwait(false);
        return all.Where(p => p.Enabled).ToList();
    }

    public async Task<ModelProvider?> GetProviderAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM model_providers WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<ModelProvider>(json, JsonOptions);
    }

    public async Task UpsertProviderAsync(ModelProvider provider, CancellationToken ct = default)
    {
        provider.UpdatedAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO model_providers (id, name, data)
            VALUES (@id, @name, @data)
            ON CONFLICT(id) DO UPDATE SET name = @name, data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", provider.Id);
        cmd.Parameters.AddWithValue("@name", provider.Name);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(provider, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Upserted model provider {ProviderId} ({ProviderName})", provider.Id, provider.Name);
    }

    public async Task DeleteProviderAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM model_providers WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Deleted model provider {ProviderId}", id);
    }
}
