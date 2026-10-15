using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteMemoryStoreContractTests : MemoryStoreContract
{
    protected override Task<StoreScope<IMemoryStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IMemoryStore>(env => new SqliteMemoryStore(env.Config));
}
