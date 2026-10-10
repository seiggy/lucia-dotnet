using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteApiKeyServiceContractTests : ApiKeyServiceContract
{
    protected override Task<StoreScope<IApiKeyService>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IApiKeyService>(
            env => new SqliteApiKeyService(env.Config, NullLogger<SqliteApiKeyService>.Instance));
}
