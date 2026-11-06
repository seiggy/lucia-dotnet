using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresActionRecordStoreTests(PostgresMigrationFixture fixture)
    : ActionRecordStoreContract
{
    protected override Task<StoreScope<IActionRecordStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IActionRecordStore>(
            fixture,
            env => new PostgresActionRecordStore(env.Config));
}
