using lucia.Data.Extensions;
using lucia.Data.PostgreSQL;
using lucia.Data.Sqlite;
using lucia.Data.Telemetry;
using Microsoft.Extensions.Hosting;

namespace lucia.Tests.Data.Telemetry;

public sealed class DataStoreHealthRegistrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lucia-health-registration-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SqliteProvider_RegistersTheSqliteProbeAndMonitor()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["DataProvider:SqlitePath"] = Path.Combine(_root, "lucia.db");

        builder.AddSqliteStoreProviders();

        using var services = builder.Services.BuildServiceProvider();
        Assert.IsType<SqliteDataStoreHealthProbe>(services.GetRequiredService<IDataStoreHealthProbe>());
        Assert.Single(services.GetServices<IHostedService>().OfType<DataStoreHealthMonitor>());
    }

    [Fact]
    public void PostgresProvider_RegistersThePostgresProbeAndMonitor()
    {
        var builder = Host.CreateApplicationBuilder();
        foreach (var name in new[] { PostgresDbNames.Config, PostgresDbNames.Traces, PostgresDbNames.Tasks })
        {
            builder.Configuration[$"ConnectionStrings:{name}"] = $"Host=127.0.0.1;Database={name};Username=lucia";
        }

        builder.AddPostgresStoreProviders();

        using var services = builder.Services.BuildServiceProvider();
        Assert.IsType<PostgresDataStoreHealthProbe>(services.GetRequiredService<IDataStoreHealthProbe>());
        Assert.Single(services.GetServices<IHostedService>().OfType<DataStoreHealthMonitor>());
    }
}
