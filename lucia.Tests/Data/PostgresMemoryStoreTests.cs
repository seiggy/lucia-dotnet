using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data;

public sealed class PostgresMemoryStoreTests(PostgresMigrationFixture fixture)
    : IClassFixture<PostgresMigrationFixture>
{
    [Fact, Trait("Category", "Integration")]
    public async Task SearchPersonalAsync_FiltersReservedHistoryBeforeTheLimit()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var migrations = new PostgresMigrationRunner(
            databases.Config, databases.Traces, databases.Tasks, NullLogger<PostgresMigrationRunner>.Instance);
        await migrations.StartAsync(CancellationToken.None);

        await PersonalMemorySearchAssertions.VerifyAsync(new PostgresMemoryStore(databases.Config));
    }
}
