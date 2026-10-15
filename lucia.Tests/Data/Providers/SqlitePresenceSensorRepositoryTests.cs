using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqlitePresenceSensorRepositoryTests : PresenceSensorRepositoryContract
{
    protected override Task<StoreScope<IPresenceSensorRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IPresenceSensorRepository>(
            env => new SqlitePresenceSensorRepository(env.Config));
}
