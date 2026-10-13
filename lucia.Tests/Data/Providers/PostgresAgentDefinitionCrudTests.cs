using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresAgentDefinitionCrudTests(PostgresMigrationFixture fixture)
    : AgentDefinitionRepositoryContract
{
    protected override Task<StoreScope<IAgentDefinitionRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IAgentDefinitionRepository>(
            fixture,
            env => new PostgresAgentDefinitionRepository(env.Config));
}
