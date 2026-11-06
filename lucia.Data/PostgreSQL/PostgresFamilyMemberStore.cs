using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Family;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace lucia.Data.PostgreSQL;

/// <summary>
/// PostgreSQL-backed family member and identity store.
/// </summary>
public sealed class PostgresFamilyMemberStore : IFamilyMemberStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresFamilyMemberStore(
        [FromKeyedServices(PostgresDbNames.Config)] PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<FamilyMember>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM family_members ORDER BY id;";

        var results = new List<FamilyMember>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var member = JsonSerializer.Deserialize<FamilyMember>(reader.GetString(0), JsonOptions);
            if (member is not null)
            {
                results.Add(member);
            }
        }

        return results;
    }

    public async Task<FamilyMember?> GetAsync(string memberId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM family_members WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", memberId);

        var json = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return json is string value ? JsonSerializer.Deserialize<FamilyMember>(value, JsonOptions) : null;
    }

    public async Task UpsertAsync(FamilyMember member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO family_members (id, data)
            VALUES (@id, @data)
            ON CONFLICT (id) DO UPDATE SET data = EXCLUDED.data;
            """;
        cmd.Parameters.AddWithValue("id", member.Id);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(member, JsonOptions)
        });

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string memberId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var identityDelete = connection.CreateCommand())
        {
            identityDelete.Transaction = transaction;
            identityDelete.CommandText = "DELETE FROM member_identities WHERE member_id = @memberId;";
            identityDelete.Parameters.AddWithValue("memberId", memberId);
            await identityDelete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var memberDelete = connection.CreateCommand())
        {
            memberDelete.Transaction = transaction;
            memberDelete.CommandText = "DELETE FROM family_members WHERE id = @memberId;";
            memberDelete.Parameters.AddWithValue("memberId", memberId);

            var affected = await memberDelete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<MemberIdentityLink>> ListIdentitiesAsync(
        string memberId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT data::text FROM member_identities WHERE member_id = @memberId ORDER BY kind, issuer, subject;";
        cmd.Parameters.AddWithValue("memberId", memberId);

        var results = new List<MemberIdentityLink>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var identity = JsonSerializer.Deserialize<MemberIdentityLink>(reader.GetString(0), JsonOptions);
            if (identity is not null)
            {
                results.Add(identity);
            }
        }

        return results;
    }

    public async Task<bool> LinkIdentityAsync(MemberIdentityLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);

        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var memberExists = connection.CreateCommand())
        {
            memberExists.Transaction = transaction;
            memberExists.CommandText = "SELECT 1 FROM family_members WHERE id = @memberId LIMIT 1;";
            memberExists.Parameters.AddWithValue("memberId", link.MemberId);
            var exists = await memberExists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (exists is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO member_identities (kind, issuer, subject, member_id, data)
            VALUES (@kind, @issuer, @subject, @memberId, @data)
            ON CONFLICT (kind, issuer, subject) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("kind", link.Kind.ToString());
        cmd.Parameters.AddWithValue("issuer", link.Issuer ?? string.Empty);
        cmd.Parameters.AddWithValue("subject", link.Subject);
        cmd.Parameters.AddWithValue("memberId", link.MemberId);
        cmd.Parameters.Add(new NpgsqlParameter("data", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(link, JsonOptions)
        });

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> UnlinkIdentityAsync(
        MemberIdentityKind kind,
        string issuer,
        string subject,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM member_identities WHERE kind = @kind AND issuer = @issuer AND subject = @subject;";
        cmd.Parameters.AddWithValue("kind", kind.ToString());
        cmd.Parameters.AddWithValue("issuer", issuer ?? string.Empty);
        cmd.Parameters.AddWithValue("subject", subject);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<FamilyMember?> FindByIdentityAsync(
        MemberIdentityKind kind,
        string issuer,
        string subject,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT family_members.data::text
            FROM member_identities
            INNER JOIN family_members ON family_members.id = member_identities.member_id
            WHERE member_identities.kind = @kind
              AND member_identities.issuer = @issuer
              AND member_identities.subject = @subject
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("kind", kind.ToString());
        cmd.Parameters.AddWithValue("issuer", issuer ?? string.Empty);
        cmd.Parameters.AddWithValue("subject", subject);

        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is string json ? JsonSerializer.Deserialize<FamilyMember>(json, JsonOptions) : null;
    }
}
