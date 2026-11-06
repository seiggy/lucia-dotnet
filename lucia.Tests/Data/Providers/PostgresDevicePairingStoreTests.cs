using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresDevicePairingStoreTests(PostgresMigrationFixture fixture)
    : DevicePairingStoreContract
{
    protected override Task<StoreScope<IDevicePairingStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IDevicePairingStore>(
            fixture,
            env => new PostgresDevicePairingStore(env.Config));
}
