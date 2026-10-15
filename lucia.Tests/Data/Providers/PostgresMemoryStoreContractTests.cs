using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresMemoryStoreContractTests(PostgresMigrationFixture fixture) : MemoryStoreContract
{
    protected override Task<StoreScope<IMemoryStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IMemoryStore>(
            fixture, env => new PostgresMemoryStore(env.Config));
}
