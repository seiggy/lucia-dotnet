using lucia.Data.Sqlite;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteSpeakerProfileStoreTests : SpeakerProfileStoreContract
{
    protected override Task<StoreScope<ISpeakerProfileStore>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<ISpeakerProfileStore>(
            env => new SqliteSpeakerProfileStore(env.Config));
}
