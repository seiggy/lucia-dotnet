using lucia.Data.PostgreSQL;
using lucia.Wyoming.Diarization;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresSpeakerProfileStoreTests(PostgresMigrationFixture fixture)
    : SpeakerProfileStoreContract
{
    protected override Task<StoreScope<ISpeakerProfileStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<ISpeakerProfileStore>(
            fixture,
            env => new PostgresSpeakerProfileStore(env.Config));
}
