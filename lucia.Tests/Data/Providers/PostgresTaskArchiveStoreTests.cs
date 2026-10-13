using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresTaskArchiveStoreTests(PostgresMigrationFixture fixture)
    : TaskArchiveStoreContract
{
    protected override Task<StoreScope<ITaskArchiveStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<ITaskArchiveStore>(
            fixture,
            env => new PostgresTaskArchiveStore(env.Tasks));
}
