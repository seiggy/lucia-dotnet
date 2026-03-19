using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Data.Sqlite;
using Microsoft.Data.Sqlite;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// SQLite-backed implementation of <see cref="IResponseTemplateRepository"/>.
/// Stores the full <see cref="ResponseTemplate"/> as JSON in the <c>data</c> column.
/// Lives in lucia.AgentHost (not lucia.Data) to avoid a circular project reference,
/// since <see cref="IResponseTemplateRepository"/> and <see cref="ResponseTemplate"/> are defined here.
/// </summary>
public sealed class SqliteResponseTemplateRepository : IResponseTemplateRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public SqliteResponseTemplateRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResponseTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM response_templates;";

        var templates = new List<ResponseTemplate>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var template = JsonSerializer.Deserialize<ResponseTemplate>(reader.GetString(0), JsonOptions);
            if (template is not null)
                templates.Add(template);
        }

        return templates;
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM response_templates WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<ResponseTemplate>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate?> GetBySkillAndActionAsync(
        string skillId,
        string action,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data FROM response_templates WHERE skill_id = @skillId AND action = @action;";
        cmd.Parameters.AddWithValue("@skillId", skillId);
        cmd.Parameters.AddWithValue("@action", action);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is not string json)
            return null;

        return JsonSerializer.Deserialize<ResponseTemplate>(json, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate> CreateAsync(ResponseTemplate template, CancellationToken ct = default)
    {
        template.Id ??= Guid.NewGuid().ToString("N");

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO response_templates (id, skill_id, action, data)
            VALUES (@id, @skillId, @action, @data);
            """;
        cmd.Parameters.AddWithValue("@id", template.Id);
        cmd.Parameters.AddWithValue("@skillId", template.SkillId);
        cmd.Parameters.AddWithValue("@action", template.Action);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(template, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return template;
    }

    /// <inheritdoc />
    public async Task<ResponseTemplate> UpdateAsync(
        string id,
        ResponseTemplate template,
        CancellationToken ct = default)
    {
        template.UpdatedAt = DateTime.UtcNow;

        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE response_templates
            SET skill_id = @skillId, action = @action, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@skillId", template.SkillId);
        cmd.Parameters.AddWithValue("@action", template.Action);
        cmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(template, JsonOptions));

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return template;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM response_templates WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync(CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM response_templates;";
            await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var defaults = DefaultResponseTemplates.GetDefaults();
        foreach (var template in defaults)
        {
            template.Id ??= Guid.NewGuid().ToString("N");

            using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO response_templates (id, skill_id, action, data)
                VALUES (@id, @skillId, @action, @data);
                """;
            insertCmd.Parameters.AddWithValue("@id", template.Id);
            insertCmd.Parameters.AddWithValue("@skillId", template.SkillId);
            insertCmd.Parameters.AddWithValue("@action", template.Action);
            insertCmd.Parameters.AddWithValue("@data", JsonSerializer.Serialize(template, JsonOptions));

            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
