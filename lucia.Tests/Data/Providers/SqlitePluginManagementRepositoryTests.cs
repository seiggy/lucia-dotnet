using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqlitePluginManagementRepositoryTests : PluginManagementRepositoryContract
{
    protected override Task<StoreScope<IPluginManagementRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IPluginManagementRepository>(
            env => new SqlitePluginManagementRepository(env.Config));
}
