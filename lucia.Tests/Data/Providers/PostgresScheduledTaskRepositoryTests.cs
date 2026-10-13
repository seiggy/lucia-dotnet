using lucia.Data.PostgreSQL;
using lucia.TimerAgent.ScheduledTasks;

namespace lucia.Tests.Data.Providers;

[Collection(PostgresStoreCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresScheduledTaskRepositoryTests(PostgresMigrationFixture fixture)
    : ScheduledTaskRepositoryContract
{
    protected override Task<StoreScope<IScheduledTaskRepository>> CreateAsync()
        => PostgresStoreEnvironment.CreateScopeAsync<IScheduledTaskRepository>(
            fixture, env => new PostgresScheduledTaskRepository(env.Tasks));
}
