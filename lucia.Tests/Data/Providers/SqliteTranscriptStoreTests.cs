using lucia.Data.Sqlite;
using lucia.Wyoming.Telemetry;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteTranscriptStoreTests : TranscriptStoreContract
{
    protected override Task<StoreScope<ITranscriptStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<ITranscriptStore>(
            env => new SqliteTranscriptStore(env.Config));
}
