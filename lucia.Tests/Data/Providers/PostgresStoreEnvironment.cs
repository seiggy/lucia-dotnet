using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Three throwaway PostgreSQL databases (config, traces, tasks) on the shared test container,
/// migrated to the production schema by <see cref="PostgresMigrationRunner"/>.
/// </summary>
public sealed class PostgresStoreEnvironment : IAsyncDisposable
{
    private readonly PostgresTestDatabases _databases;

    private PostgresStoreEnvironment(PostgresTestDatabases databases)
    {
        _databases = databases;
    }

    public PostgresConnectionFactory Config => _databases.Config;

    public PostgresConnectionFactory Traces => _databases.Traces;

    public PostgresConnectionFactory Tasks => _databases.Tasks;

    public static async Task<PostgresStoreEnvironment> CreateAsync(PostgresMigrationFixture fixture)
    {
        var databases = await fixture.CreateDatabasesAsync();
        await new PostgresMigrationRunner(
                databases.Config,
                databases.Traces,
                databases.Tasks,
                NullLogger<PostgresMigrationRunner>.Instance)
            .StartAsync(CancellationToken.None);

        return new PostgresStoreEnvironment(databases);
    }

    /// <summary>Creates an environment and wraps the store built by <paramref name="create"/> in a scope.</summary>
    public static async Task<StoreScope<T>> CreateScopeAsync<T>(
        PostgresMigrationFixture fixture,
        Func<PostgresStoreEnvironment, T> create)
    {
        var environment = await CreateAsync(fixture);
        return new StoreScope<T>(create(environment), environment.DisposeAsync);
    }

    public ValueTask DisposeAsync() => _databases.DisposeAsync();
}
