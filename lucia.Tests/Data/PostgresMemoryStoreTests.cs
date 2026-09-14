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

    [Fact, Trait("Category", "Integration")]
    public async Task SearchAsync_TreatsWildcardCharactersAsLiterals()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var migrations = new PostgresMigrationRunner(
            databases.Config, databases.Traces, databases.Tasks, NullLogger<PostgresMigrationRunner>.Instance);
        await migrations.StartAsync(CancellationToken.None);

        await PersonalMemorySearchAssertions.VerifyLiteralSearchSemanticsAsync(new PostgresMemoryStore(databases.Config));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task StoreAsync_WithAbsoluteExpiresAt_PersistsExactDeadline()
    {
        await using var databases = await fixture.CreateDatabasesAsync();
        var migrations = new PostgresMigrationRunner(
            databases.Config, databases.Traces, databases.Tasks, NullLogger<PostgresMigrationRunner>.Instance);
        await migrations.StartAsync(CancellationToken.None);

        var store = new PostgresMemoryStore(databases.Config);
        var expiresAt = new DateTimeOffset(2030, 2, 3, 4, 5, 6, 987, TimeSpan.FromHours(-7));

        await store.StoreAsync("user-1", "deadline", "value", expiresAt, CancellationToken.None);

        var entry = Assert.Single(await store.GetAllAsync("user-1"));
        Assert.Equal(expiresAt.UtcDateTime, entry.ExpiresAt);

        await store.StoreAsync("user-1", "deadline", "edited", new DateTimeOffset(entry.ExpiresAt!.Value), CancellationToken.None);
        var edited = Assert.Single(await store.GetAllAsync("user-1"));
        Assert.Equal("edited", edited.Value);
        Assert.Equal(entry.ExpiresAt, edited.ExpiresAt);
    }
}
