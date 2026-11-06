using lucia.Agents.Abstractions;
using lucia.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteFamilyMemberStoreTests : FamilyMemberStoreContract
{
    protected override Task<StoreScope<IFamilyMemberStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IFamilyMemberStore>(
            env => new SqliteFamilyMemberStore(env.Config));
}
