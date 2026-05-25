using lucia.Agents.CommandTracing;
using lucia.Agents.Training;
using lucia.Data.Extensions;
using lucia.Data.PostgreSQL;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace lucia.Tests.Data;

public sealed class PostgresStoreProviderRegistrationTests
{
    [Fact]
    public void AddPostgresStoreProviders_RegistersPostgresRepositoriesForTraceAndTaskStores()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:luciaconfig"] = "Host=localhost;Database=luciaconfig;Username=test;Password=test",
            ["ConnectionStrings:luciatraces"] = "Host=localhost;Database=luciatraces;Username=test;Password=test",
            ["ConnectionStrings:luciatasks"] = "Host=localhost;Database=luciatasks;Username=test;Password=test",
        });

        builder.AddPostgresStoreProviders();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(ITraceRepository)
                && descriptor.ImplementationType == typeof(PostgresTraceRepository));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(ICommandTraceRepository)
                && descriptor.ImplementationType == typeof(PostgresCommandTraceRepository));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IScheduledTaskRepository)
                && descriptor.ImplementationType == typeof(PostgresScheduledTaskRepository));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IAlarmClockRepository)
                && descriptor.ImplementationType == typeof(PostgresAlarmClockRepository));
    }
}
