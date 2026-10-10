using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresVisibilityConfigStoreTests(PostgresMigrationFixture fixture)
    : VisibilityConfigStoreContract
{
    protected override Task<StoreScope<IConfigStoreWriter>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IConfigStoreWriter>(
            fixture,
            env => new PostgresConfigStoreWriter(env.Config));
}