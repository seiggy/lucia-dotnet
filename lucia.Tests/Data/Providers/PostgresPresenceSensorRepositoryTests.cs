using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresPresenceSensorRepositoryTests(PostgresMigrationFixture fixture)
    : PresenceSensorRepositoryContract
{
    protected override Task<StoreScope<IPresenceSensorRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IPresenceSensorRepository>(
            fixture, env => new PostgresPresenceSensorRepository(env.Config));
}
