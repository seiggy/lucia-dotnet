using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteLlmUsageStoreTests : LlmUsageStoreContract
{
    protected override Task<StoreScope<ILlmUsageStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<ILlmUsageStore>(
            env => new SqliteLlmUsageStore(env.Config));
}
