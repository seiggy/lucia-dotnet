using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteModelProviderRepositoryTests : ModelProviderRepositoryContract
{
    protected override Task<StoreScope<IModelProviderRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IModelProviderRepository>(
            env => new SqliteModelProviderRepository(env.Config, NullLogger<SqliteModelProviderRepository>.Instance));
}
