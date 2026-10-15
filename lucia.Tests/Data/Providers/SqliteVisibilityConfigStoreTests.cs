using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteVisibilityConfigStoreTests : VisibilityConfigStoreContract
{
    protected override Task<StoreScope<IConfigStoreWriter>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IConfigStoreWriter>(
            env => new SqliteConfigStoreWriter(env.Config));
}