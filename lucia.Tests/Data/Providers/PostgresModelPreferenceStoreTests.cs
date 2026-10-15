using lucia.Data.PostgreSQL;
using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresModelPreferenceStoreTests(PostgresMigrationFixture fixture)
    : ModelPreferenceStoreContract
{
    protected override Task<StoreScope<IModelPreferenceStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IModelPreferenceStore>(
            fixture,
            env => new PostgresModelPreferenceStore(env.Config, NullLogger<PostgresModelPreferenceStore>.Instance));
}
