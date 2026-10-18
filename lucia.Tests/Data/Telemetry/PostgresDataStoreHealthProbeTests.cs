using lucia.Data.PostgreSQL;
using lucia.Data.Telemetry;
using lucia.Tests.Data.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Telemetry;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresDataStoreHealthProbeTests(PostgresMigrationFixture fixture)
{
    [Fact]
    public async Task ProbeAsync_ReportsSizeAndConnectionsForEachDatabase()
    {
        await using var environment = await PostgresStoreEnvironment.CreateAsync(fixture);
        var probe = new PostgresDataStoreHealthProbe(
            new Dictionary<string, PostgresConnectionFactory>
            {
                [PostgresDbNames.Config] = environment.Config,
                [PostgresDbNames.Traces] = environment.Traces,
                [PostgresDbNames.Tasks] = environment.Tasks,
            },
            NullLogger<PostgresDataStoreHealthProbe>.Instance);

        var samples = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal("postgresql", probe.System);
        Assert.Equal(3, samples.Count);
        Assert.All(samples, sample =>
        {
            Assert.True(sample.Up);
            Assert.True(sample.SizeBytes > 0);
            Assert.True(sample.Connections >= 1);
            Assert.Null(sample.WalBytes);
        });
    }

    [Fact]
    public async Task ProbeAsync_ReportsDownWhenTheServerIsUnreachable()
    {
        await using var unreachable = new PostgresConnectionFactory(
            "Host=127.0.0.1;Port=1;Database=luciaconfig;Username=lucia;Password=unused;Timeout=1;Pooling=false");
        var probe = new PostgresDataStoreHealthProbe(
            new Dictionary<string, PostgresConnectionFactory> { [PostgresDbNames.Config] = unreachable },
            NullLogger<PostgresDataStoreHealthProbe>.Instance);

        var sample = Assert.Single(await probe.ProbeAsync(CancellationToken.None));

        Assert.False(sample.Up);
        Assert.Null(sample.SizeBytes);
    }
}
