using lucia.Agents.CommandTracing;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresCommandTraceRepositoryContractTests(PostgresMigrationFixture fixture)
    : CommandTraceRepositoryContract
{
    protected override Task<StoreScope<ICommandTraceRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<ICommandTraceRepository>(
            fixture,
            env => new PostgresCommandTraceRepository(env.Traces));
}
