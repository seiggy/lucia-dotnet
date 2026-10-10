using lucia.Data.Sqlite;
using lucia.Wyoming.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteModelPreferenceStoreTests : ModelPreferenceStoreContract
{
    protected override Task<StoreScope<IModelPreferenceStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IModelPreferenceStore>(
            env => new SqliteModelPreferenceStore(env.Config, NullLogger<SqliteModelPreferenceStore>.Instance));
}
