using lucia.AgentHost.Startup;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests.Startup;

public sealed class LegacyMongoGuardTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void ThrowIfConfigured_StoreIsMongoDb_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMongoGuard.ThrowIfConfigured(Config(("DataProvider:Store", "mongodb"))));

        Assert.Contains("DataProvider:Store", ex.Message);
    }

    [Theory]
    [InlineData("mongodb://mongo:27017/luciaconfig")]
    [InlineData("MONGODB+SRV://cluster.example.net/lucia")]
    public void ThrowIfConfigured_MongoConnectionString_Throws(string connectionString)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LegacyMongoGuard.ThrowIfConfigured(Config(("ConnectionStrings:luciaconfig", connectionString))));

        Assert.Contains("ConnectionStrings:luciaconfig", ex.Message);
    }

    [Fact]
    public void ThrowIfConfigured_SqliteAndPostgres_DoesNotThrow()
    {
        LegacyMongoGuard.ThrowIfConfigured(Config(("DataProvider:Store", "SQLite")));
        LegacyMongoGuard.ThrowIfConfigured(Config(
            ("DataProvider:Store", "PostgreSQL"),
            ("ConnectionStrings:luciaconfig", "Host=postgres;Database=luciaconfig"),
            ("ConnectionStrings:redis", "redis:6379")));
        LegacyMongoGuard.ThrowIfConfigured(Config());
    }
}
