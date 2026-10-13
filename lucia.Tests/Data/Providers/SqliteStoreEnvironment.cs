using lucia.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

/// <summary>
/// Three temporary SQLite databases (config, traces, tasks) with the production schema applied
/// by <see cref="SqliteMigrationRunner"/>, so store tests exercise the real tables.
/// </summary>
public sealed class SqliteStoreEnvironment : IDisposable
{
    private readonly string _directory;

    private SqliteStoreEnvironment(
        string directory,
        SqliteConnectionFactory config,
        SqliteConnectionFactory traces,
        SqliteConnectionFactory tasks)
    {
        _directory = directory;
        Config = config;
        Traces = traces;
        Tasks = tasks;
    }

    public SqliteConnectionFactory Config { get; }

    public SqliteConnectionFactory Traces { get; }

    public SqliteConnectionFactory Tasks { get; }

    public static async Task<SqliteStoreEnvironment> CreateAsync()
    {
        var directory = Directory.CreateTempSubdirectory("lucia-store-test-").FullName;
        var config = new SqliteConnectionFactory(Path.Combine(directory, "config.db"));
        var traces = new SqliteConnectionFactory(Path.Combine(directory, "traces.db"));
        var tasks = new SqliteConnectionFactory(Path.Combine(directory, "tasks.db"));

        await new SqliteMigrationRunner(config, traces, tasks, NullLogger<SqliteMigrationRunner>.Instance)
            .StartAsync(CancellationToken.None);

        return new SqliteStoreEnvironment(directory, config, traces, tasks);
    }

    /// <summary>Creates an environment and wraps the store built by <paramref name="create"/> in a scope.</summary>
    public static async Task<StoreScope<T>> CreateScopeAsync<T>(Func<SqliteStoreEnvironment, T> create)
    {
        var environment = await CreateAsync();
        return new StoreScope<T>(create(environment), () =>
        {
            environment.Dispose();
            return ValueTask.CompletedTask;
        });
    }

    public void Dispose()
    {
        Config.Dispose();
        Traces.Dispose();
        Tasks.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a lingering WAL handle must not fail the test.
        }
    }
}
