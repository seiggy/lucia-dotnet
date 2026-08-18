using Testcontainers.PostgreSql;

namespace lucia.Tests.Data;

public sealed class PostgresMigrationFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();

    public Task<PostgresTestDatabases> CreateDatabasesAsync()
        => PostgresTestDatabases.CreateAsync(Container.GetConnectionString());
}
