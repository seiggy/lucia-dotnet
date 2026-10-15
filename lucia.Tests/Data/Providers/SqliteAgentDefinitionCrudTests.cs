using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteAgentDefinitionCrudTests : AgentDefinitionRepositoryContract
{
    protected override Task<StoreScope<IAgentDefinitionRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IAgentDefinitionRepository>(
            env => new SqliteAgentDefinitionRepository(env.Config));
}
