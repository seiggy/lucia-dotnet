using lucia.Agents.CommandTracing;
using lucia.Agents.Training;
using lucia.Data.Extensions;
using lucia.Data.InMemory;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace lucia.Tests.Data;

public sealed class PostgresStoreProviderRegistrationTests
{
    [Fact]
    public void AddPostgresStoreProviders_RegistersFallbackRepositoriesForUnsupportedStores()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProvider:PostgresConnectionString"] = "Host=localhost;Database=lucia;Username=test;Password=test",
        });

        builder.AddPostgresStoreProviders();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(ITraceRepository)
                && descriptor.ImplementationType == typeof(InMemoryTraceRepository));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(ICommandTraceRepository)
                && descriptor.ImplementationType == typeof(InMemoryCommandTraceRepository));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IScheduledTaskRepository)
                && descriptor.ImplementationType == typeof(InMemoryScheduledTaskRepository));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IAlarmClockRepository)
                && descriptor.ImplementationType == typeof(InMemoryAlarmClockRepository));
    }
}
