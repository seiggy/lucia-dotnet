using lucia.Data.PostgreSQL;
using lucia.Wyoming.Telemetry;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresTranscriptStoreTests(PostgresMigrationFixture fixture)
    : TranscriptStoreContract
{
    protected override Task<StoreScope<ITranscriptStore>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<ITranscriptStore>(
            fixture,
            env => new PostgresTranscriptStore(env.Config));
}
