using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresPluginManagementRepositoryTests(PostgresMigrationFixture fixture)
    : PluginManagementRepositoryContract
{
    protected override Task<StoreScope<IPluginManagementRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IPluginManagementRepository>(
            fixture,
            env => new PostgresPluginManagementRepository(env.Config));
}
