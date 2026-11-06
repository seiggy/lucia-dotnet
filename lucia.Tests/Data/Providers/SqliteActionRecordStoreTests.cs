using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteActionRecordStoreTests : ActionRecordStoreContract
{
    protected override Task<StoreScope<IActionRecordStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IActionRecordStore>(
            env => new SqliteActionRecordStore(env.Config));
}
