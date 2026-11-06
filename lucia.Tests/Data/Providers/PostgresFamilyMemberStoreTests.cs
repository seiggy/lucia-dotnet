using lucia.Agents.Abstractions;
using lucia.Data.PostgreSQL;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresFamilyMemberStoreTests(PostgresMigrationFixture fixture)
    : FamilyMemberStoreContract
{
    protected override Task<StoreScope<IFamilyMemberStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IFamilyMemberStore>(
            fixture,
            env => new PostgresFamilyMemberStore(env.Config));
}
