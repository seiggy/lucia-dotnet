using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresApiKeyServiceContractTests(PostgresMigrationFixture fixture) : ApiKeyServiceContract
{
    protected override Task<StoreScope<IApiKeyService>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IApiKeyService>(
            fixture, env => new PostgresApiKeyService(env.Config, NullLogger<PostgresApiKeyService>.Instance));
}
