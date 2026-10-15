using lucia.AgentHost.Conversation.Templates;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresResponseTemplateRepositoryTests(PostgresMigrationFixture fixture)
    : ResponseTemplateRepositoryContract
{
    protected override Task<StoreScope<IResponseTemplateRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IResponseTemplateRepository>(
            fixture, env => new PostgresResponseTemplateRepository(env.Config));
}
