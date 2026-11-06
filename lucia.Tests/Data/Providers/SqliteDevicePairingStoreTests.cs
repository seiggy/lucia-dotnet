using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteDevicePairingStoreTests : DevicePairingStoreContract
{
    protected override Task<StoreScope<IDevicePairingStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IDevicePairingStore>(
            env => new SqliteDevicePairingStore(env.Config));
}
