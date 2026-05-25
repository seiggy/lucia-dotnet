using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.Data;
using lucia.Data.PostgreSQL;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;
using NpgsqlTypes;

namespace lucia.AgentHost.Conversation.Templates;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IResponseTemplateRepository"/>.
/// Stores the full <see cref="ResponseTemplate"/> as JSON in the <c>data</c> column.
/// </summary>
public sealed class PostgresResponseTemplateRepository : IResponseTemplateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresResponseTemplateRepository(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ResponseTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM response_templates ORDER BY skill_id, action;";

        var templates = new List<ResponseTemplate>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var template = JsonSerializer.Deserialize<ResponseTemplate>(reader.GetString(0), JsonOptions);
            if (template is not null)
            {
                templates.Add(template);
            }
        }

        return templates;
    }

    public async Task<ResponseTemplate?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM response_templates WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ResponseTemplate>(json, JsonOptions) : null;
    }

    public async Task<ResponseTemplate?> GetBySkillAndActionAsync(
        string skillId,
        string action,
        CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM response_templates WHERE skill_id = @skillId AND action = @action;";
        cmd.Parameters.AddWithValue("skillId", skillId);
        cmd.Parameters.AddWithValue("action", action);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<ResponseTemplate>(json, JsonOptions) : null;
    }

    public async Task<ResponseTemplate> CreateAsync(ResponseTemplate template, CancellationToken ct = default)
    {
        template.Id ??= Guid.NewGuid().ToString("N");

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO response_templates (id, skill_id, action, data)
            VALUES (@id, @skillId, @action, @data);
            """;
        cmd.Parameters.AddWithValue("id", template.Id);
        cmd.Parameters.AddWithValue("skillId", template.SkillId);
        cmd.Parameters.AddWithValue("action", template.Action);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(template, JsonOptions),
        });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return template;
    }

    public async Task<ResponseTemplate> UpdateAsync(string id, ResponseTemplate template, CancellationToken ct = default)
    {
        template.UpdatedAt = DateTime.UtcNow;

        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE response_templates
            SET skill_id = @skillId, action = @action, data = @data
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("skillId", template.SkillId);
        cmd.Parameters.AddWithValue("action", template.Action);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(template, JsonOptions),
        });

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return template;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM response_templates WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);

        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task ResetToDefaultsAsync(CancellationToken ct = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM response_templates;";
            await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var template in DefaultResponseTemplates.GetDefaults())
        {
            template.Id ??= Guid.NewGuid().ToString("N");

            await using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO response_templates (id, skill_id, action, data)
                VALUES (@id, @skillId, @action, @data);
                """;
            insertCmd.Parameters.AddWithValue("id", template.Id);
            insertCmd.Parameters.AddWithValue("skillId", template.SkillId);
            insertCmd.Parameters.AddWithValue("action", template.Action);
            insertCmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(template, JsonOptions),
            });

            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
