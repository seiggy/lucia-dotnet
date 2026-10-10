using lucia.Agents.CommandTracing;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteCommandTraceRepositoryContractTests : CommandTraceRepositoryContract
{
    protected override Task<StoreScope<ICommandTraceRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<ICommandTraceRepository>(
            env => new SqliteCommandTraceRepository(env.Traces));
}
