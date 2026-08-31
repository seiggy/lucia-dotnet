using lucia.Agents.Auth;
using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data;

public sealed class PostgresApiKeyServiceTests(PostgresMigrationFixture fixture)
    : IClassFixture<PostgresMigrationFixture>
{
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
}
