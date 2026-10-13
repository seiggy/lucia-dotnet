using lucia.Agents.Training;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresTraceRepositoryTests(PostgresMigrationFixture fixture)
    : TraceRepositoryContract
{
    protected override Task<StoreScope<ITraceRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<ITraceRepository>(
            fixture,
            env => new PostgresTraceRepository(env.Traces));
}
