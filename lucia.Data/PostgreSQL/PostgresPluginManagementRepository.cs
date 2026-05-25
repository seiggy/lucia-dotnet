using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.PluginFramework;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IPluginManagementRepository"/>.
/// Stores plugin repository definitions and installed plugin records as JSON blobs.
/// </summary>
public sealed class PostgresPluginManagementRepository : IPluginManagementRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresPluginManagementRepository([FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<PluginRepositoryDefinition>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM plugin_repositories;";

        var repositories = new List<PluginRepositoryDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var repository = JsonSerializer.Deserialize<PluginRepositoryDefinition>(reader.GetString(0), JsonOptions);
            if (repository is not null)
            {
                repositories.Add(repository);
            }
        }

        return repositories;
    }

    public async Task<PluginRepositoryDefinition?> GetRepositoryAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM plugin_repositories WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<PluginRepositoryDefinition>(json, JsonOptions) : null;
    }

    public async Task UpsertRepositoryAsync(PluginRepositoryDefinition repo, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plugin_repositories (id, data)
            VALUES (@id, @data)
            ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", repo.Id);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(repo, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteRepositoryAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM plugin_repositories WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<InstalledPluginRecord>> GetInstalledPluginsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM installed_plugins;";

        var plugins = new List<InstalledPluginRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var plugin = JsonSerializer.Deserialize<InstalledPluginRecord>(reader.GetString(0), JsonOptions);
            if (plugin is not null)
            {
                plugins.Add(plugin);
            }
        }

        return plugins;
    }

    public async Task<InstalledPluginRecord?> GetInstalledPluginAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM installed_plugins WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<InstalledPluginRecord>(json, JsonOptions) : null;
    }

    public async Task UpsertInstalledPluginAsync(InstalledPluginRecord record, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO installed_plugins (id, data)
            VALUES (@id, @data)
            ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(record, JsonOptions) });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteInstalledPluginAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM installed_plugins WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
