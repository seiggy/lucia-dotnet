using lucia.Data.PostgreSQL;
using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresAlarmClockRepositoryTests(PostgresMigrationFixture fixture)
    : AlarmClockRepositoryContract
{
    protected override Task<StoreScope<IAlarmClockRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IAlarmClockRepository>(
            fixture, env => new PostgresAlarmClockRepository(env.Tasks));
}
