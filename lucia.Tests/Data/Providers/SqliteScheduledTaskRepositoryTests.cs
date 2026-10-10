using lucia.Data.Sqlite;
using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.Data.Providers;

public sealed class SqliteScheduledTaskRepositoryTests : ScheduledTaskRepositoryContract
{
    protected override Task<StoreScope<IScheduledTaskRepository>> CreateAsync()
        => SqliteStoreEnvironment.CreateScopeAsync<IScheduledTaskRepository>(
            env => new SqliteScheduledTaskRepository(env.Tasks));
}
