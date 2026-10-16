using lucia.Data.Sqlite;
using lucia.Data.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Telemetry;

public sealed class SqliteDataStoreHealthProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lucia-sqlite-health-").FullName;
    private readonly List<SqliteConnectionFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ProbeAsync_ReportsSizeAndWalForEachDatabase()
    {
        var basePath = Path.Combine(_root, "lucia.db");
        string[] names = [SqliteDbNames.Config, SqliteDbNames.Traces, SqliteDbNames.Tasks];
        var factories = names.ToDictionary(
            name => name,
            name => Track(new SqliteConnectionFactory(SqliteDbNames.GetPath(basePath, name))));
        using (var connection = factories[SqliteDbNames.Config].CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE sample (id INTEGER); INSERT INTO sample VALUES (1);";
            command.ExecuteNonQuery();
        }
        var probe = new SqliteDataStoreHealthProbe(factories, NullLogger<SqliteDataStoreHealthProbe>.Instance);

        var samples = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal("sqlite", probe.System);
        Assert.Equal(names.Order(), samples.Select(sample => sample.Database).Order());
        Assert.All(samples, sample =>
        {
            Assert.True(sample.Up);
            Assert.True(sample.SizeBytes > 0);
            Assert.NotNull(sample.WalBytes);
            Assert.Null(sample.Connections);
        });
        Assert.True(samples.Single(sample => sample.Database == SqliteDbNames.Config).WalBytes > 0);
    }

    [Fact]
    public async Task ProbeAsync_ReportsDownWhenTheDatabaseCannotOpen()
    {
        var blocker = Path.Combine(_root, "not-a-directory");
        await File.WriteAllTextAsync(blocker, "x");
        var factories = new Dictionary<string, SqliteConnectionFactory>
        {
            [SqliteDbNames.Config] = Track(new SqliteConnectionFactory(Path.Combine(blocker, "lucia-config.db"))),
        };
        var probe = new SqliteDataStoreHealthProbe(factories, NullLogger<SqliteDataStoreHealthProbe>.Instance);

        var sample = Assert.Single(await probe.ProbeAsync(CancellationToken.None));

        Assert.False(sample.Up);
        Assert.Equal(SqliteDbNames.Config, sample.Database);
    }

    private SqliteConnectionFactory Track(SqliteConnectionFactory factory)
    {
        _factories.Add(factory);
        return factory;
    }
}
