using lucia.Data.Sqlite;
using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteAlarmClockRepositoryTests : AlarmClockRepositoryContract
{
    protected override Task<StoreScope<IAlarmClockRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IAlarmClockRepository>(
            env => new SqliteAlarmClockRepository(env.Tasks));
}
