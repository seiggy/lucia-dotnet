using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresModelProviderRepositoryTests(PostgresMigrationFixture fixture)
    : ModelProviderRepositoryContract
{
    protected override Task<StoreScope<IModelProviderRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IModelProviderRepository>(
            fixture,
            env => new PostgresModelProviderRepository(env.Config, NullLogger<PostgresModelProviderRepository>.Instance));
}
