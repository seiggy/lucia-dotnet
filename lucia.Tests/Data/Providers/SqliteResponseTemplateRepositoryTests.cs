using lucia.AgentHost.Conversation.Templates;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteResponseTemplateRepositoryTests : ResponseTemplateRepositoryContract
{
    protected override Task<StoreScope<IResponseTemplateRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IResponseTemplateRepository>(
            env => new SqliteResponseTemplateRepository(env.Config));
}
