using lucia.Agents.Training;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteTraceRepositoryTests : TraceRepositoryContract
{
    protected override Task<StoreScope<ITraceRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<ITraceRepository>(
            env => new SqliteTraceRepository(env.Traces));
}
