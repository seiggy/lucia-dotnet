using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteConfigStoreWriterContractTests : ConfigStoreWriterContract
{
    protected override Task<StoreScope<IConfigStoreWriter>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IConfigStoreWriter>(
            env => new SqliteConfigStoreWriter(env.Config));

    [Fact]
    public async Task ConfigurationProvider_LoadsValuesAfterReload()
    {
        using var environment = await SqliteStoreEnvironment.CreateAsync();
        var writer = new SqliteConfigStoreWriter(environment.Config);
        await writer.SetAsync("Feature:Flag", "enabled", cancellationToken: CancellationToken.None);

        var configuration = new ConfigurationBuilder()
            .AddSqliteConfiguration(environment.Config)
            .Build();

        configuration.Reload();

        Assert.Equal("enabled", configuration["Feature:Flag"]);
    }
}
