using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresConfigStoreWriterContractTests(PostgresMigrationFixture fixture)
    : ConfigStoreWriterContract
{
    protected override Task<StoreScope<IConfigStoreWriter>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IConfigStoreWriter>(
            fixture,
            env => new PostgresConfigStoreWriter(env.Config));

    [Fact]
    public async Task ConfigurationProvider_LoadsValuesAfterReload()
    {
        await using var environment = await PostgresStoreEnvironment.CreateAsync(fixture);

        var writer = new PostgresConfigStoreWriter(environment.Config);
        await writer.SetAsync("Feature:Flag", "enabled", cancellationToken: CancellationToken.None);

        var configuration = new ConfigurationBuilder()
            .AddPostgresConfiguration(environment.Config)
            .Build();

        configuration.Reload();

        Assert.Equal("enabled", configuration["Feature:Flag"]);
    }
}
