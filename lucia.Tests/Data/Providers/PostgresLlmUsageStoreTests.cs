using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresLlmUsageStoreTests(PostgresMigrationFixture fixture)
    : LlmUsageStoreContract
{
    protected override Task<StoreScope<ILlmUsageStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<ILlmUsageStore>(
            fixture,
            env => new PostgresLlmUsageStore(env.Config));
}
