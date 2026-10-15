using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteTaskArchiveStoreTests : TaskArchiveStoreContract
{
    protected override Task<StoreScope<ITaskArchiveStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<ITaskArchiveStore>(
            env => new SqliteTaskArchiveStore(env.Tasks));
}
