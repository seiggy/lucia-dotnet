using lucia.Agents.Auth;
using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace lucia.Tests.Data;

public sealed class PostgresApiKeyServiceTests(PostgresMigrationFixture fixture)
    : IClassFixture<PostgresMigrationFixture>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAdministratorKeyIfNoneAsync_ConcurrentCalls_CreateOne()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var service = await CreateServiceAsync(databases);

        var results = await Task.WhenAll(
            service.CreateAdministratorKeyIfNoneAsync("Dashboard"),
            service.CreateAdministratorKeyIfNoneAsync("Dashboard"));

        Assert.Single(results, result => result is not null);
        Assert.Single(
            await service.ListKeysAsync(),
            key => !key.IsRevoked
                && key.Scopes.Contains(AuthOptions.AdministratorScope));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateAdministratorKeyIfNoneAsync_LegacyDashboardKey_ReturnsNull()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var service = await CreateServiceAsync(databases);
        _ = await service.CreateKeyAsync("Dashboard");

        var result = await service.CreateAdministratorKeyIfNoneAsync(
            "Dashboard");

        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeKeyAsync_ThrowsWhenRevokingLastAdministratorKey()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var migration = new PostgresMigrationRunner(
            databases.Config,
            databases.Traces,
            databases.Tasks,
            NullLogger<PostgresMigrationRunner>.Instance);
        await migration.StartAsync(CancellationToken.None);
        var service = new PostgresApiKeyService(
            databases.Config,
            NullLogger<PostgresApiKeyService>.Instance);
        var administrator = await service.CreateKeyAsync(
            "Owner",
            isAdministrator: true);
        _ = await service.CreateKeyAsync("Ordinary");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RevokeKeyAsync(administrator.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeKeyAsync_AllowsExpiredAdministratorKey()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var service = await CreateServiceAsync(databases);
        _ = await service.CreateKeyAsync("Owner", isAdministrator: true);
        var expired = await service.CreateKeyAsync(
            "Expired owner",
            isAdministrator: true);
        await using (var connection =
            await databases.Config.CreateConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE api_keys
                SET expires_at = @expiresAt
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue(
                "expiresAt",
                DateTime.UtcNow.AddMinutes(-1));
            command.Parameters.AddWithValue("id", expired.Id);
            await command.ExecuteNonQueryAsync();
        }

        Assert.True(await service.RevokeKeyAsync(expired.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RevokeKeyAsync_ConcurrentAdministrators_PreservesOne()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var service = await CreateServiceAsync(databases);
        var first = await service.CreateKeyAsync(
            "First owner",
            isAdministrator: true);
        var second = await service.CreateKeyAsync(
            "Second owner",
            isAdministrator: true);
        _ = await service.CreateKeyAsync("Ordinary");

        var outcomes = await Task.WhenAll(
            RevokeOrRejectAsync(first.Id),
            RevokeOrRejectAsync(second.Id));
        var keys = await service.ListKeysAsync();

        Assert.Single(outcomes, outcome => outcome);
        Assert.Single(keys, key =>
            !key.IsRevoked
            && key.Scopes.Contains(AuthOptions.AdministratorScope));

        async Task<bool> RevokeOrRejectAsync(string id)
        {
            try
            {
                return await service.RevokeKeyAsync(id);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RegenerateKeyAsync_RevokeFailure_RollsBackReplacement()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var service = await CreateServiceAsync(databases);
        var original = await service.CreateKeyAsync(
            "Owner",
            isAdministrator: true);
        await using (var connection =
            await databases.Config.CreateConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE FUNCTION reject_api_key_revoke()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'simulated revoke failure';
                END;
                $$;
                CREATE TRIGGER reject_api_key_revoke
                BEFORE UPDATE OF is_revoked ON api_keys
                FOR EACH ROW
                WHEN (NEW.is_revoked = TRUE)
                EXECUTE FUNCTION reject_api_key_revoke();
                """;
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<PostgresException>(
            () => service.RegenerateKeyAsync(original.Id));

        Assert.NotNull(await service.ValidateKeyAsync(original.Key));
        Assert.Single(
            await service.ListKeysAsync(),
            key => !key.IsRevoked);
    }

    private static async Task<PostgresApiKeyService> CreateServiceAsync(
        PostgresTestDatabases databases)
    {
        var migration = new PostgresMigrationRunner(
            databases.Config,
            databases.Traces,
            databases.Tasks,
            NullLogger<PostgresMigrationRunner>.Instance);
        await migration.StartAsync(CancellationToken.None);
        return new PostgresApiKeyService(
            databases.Config,
            NullLogger<PostgresApiKeyService>.Instance);
    }
}
