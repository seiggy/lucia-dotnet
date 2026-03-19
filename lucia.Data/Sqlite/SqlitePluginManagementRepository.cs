using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Agents.Abstractions;
using lucia.Agents.PluginFramework;
using Microsoft.Data.Sqlite;

namespace lucia.Data.Sqlite;

/// <summary>
/// SQLite-backed implementation of <see cref="IPluginManagementRepository"/>.
/// Stores plugin repository definitions and installed plugin records as JSON blobs.
/// </summary>
public sealed class SqlitePluginManagementRepository : IPluginManagementRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public SqlitePluginManagementRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ── Plugin Repositories ─────────────────────────────────────

    public async Task<List<PluginRepositoryDefinition>> GetRepositoriesAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM plugin_repositories;";

        var repos = new List<PluginRepositoryDefinition>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var repo = JsonSerializer.Deserialize<PluginRepositoryDefinition>(reader.GetString(0), JsonOptions);
            if (repo is not null)
                repos.Add(repo);
        }

        return repos;
    }

    public async Task<PluginRepositoryDefinition?> GetRepositoryAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM plugin_repositories WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<PluginRepositoryDefinition>(json, JsonOptions);
    }

    public async Task UpsertRepositoryAsync(PluginRepositoryDefinition repo, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO plugin_repositories (id, data)
            VALUES (@id, @data)
            ON CONFLICT(id) DO UPDATE SET data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", repo.Id);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(repo, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteRepositoryAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM plugin_repositories WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Installed Plugins ───────────────────────────────────────

    public async Task<List<InstalledPluginRecord>> GetInstalledPluginsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM installed_plugins;";

        var plugins = new List<InstalledPluginRecord>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var plugin = JsonSerializer.Deserialize<InstalledPluginRecord>(reader.GetString(0), JsonOptions);
            if (plugin is not null)
                plugins.Add(plugin);
        }

        return plugins;
    }

    public async Task<InstalledPluginRecord?> GetInstalledPluginAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM installed_plugins WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<InstalledPluginRecord>(json, JsonOptions);
    }

    public async Task UpsertInstalledPluginAsync(InstalledPluginRecord record, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO installed_plugins (id, data)
            VALUES (@id, @data)
            ON CONFLICT(id) DO UPDATE SET data = @data;
            """;
        cmd.Parameters.AddWithValue("@id", record.Id);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(record, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteInstalledPluginAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM installed_plugins WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
