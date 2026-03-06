using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.Configuration;
using lucia.Agents.DataStores;
using lucia.Agents.Integration;
using lucia.Agents.PluginFramework;
using lucia.Agents.Training;
using lucia.Data.Configuration;
using lucia.Data.Repositories;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace lucia.Data.Extensions;

/// <summary>
/// Extension methods for registering the Lucia data provider.
/// </summary>
public static class DataProviderExtensions
{
    /// <summary>
    /// Registers the data provider based on the LUCIA_DB_PROVIDER environment variable.
    /// For MongoDB (default), registers MongoDB implementations.
    /// For SQLite/PostgreSQL, registers EF Core implementations with LuciaDbContext.
    /// </summary>
    public static IHostApplicationBuilder AddLuciaDataProvider(this IHostApplicationBuilder builder)
    {
        var providerType = DataProviderOptions.ParseFromEnvironment();

        switch (providerType)
        {
            case DataProviderType.Sqlite:
                AddEfCoreProvider(builder, options =>
                {
                    var connectionString = builder.Configuration["DataProvider:SqliteConnectionString"]
                        ?? "Data Source=lucia.db";
                    options.UseSqlite(connectionString);
                });
                break;

            case DataProviderType.PostgreSql:
                AddEfCoreProvider(builder, options =>
                {
                    var connectionString = builder.Configuration["DataProvider:PostgreSqlConnectionString"]
                        ?? builder.Configuration.GetConnectionString("luciadb")
                        ?? throw new InvalidOperationException(
                            "PostgreSQL connection string not found. Set DataProvider:PostgreSqlConnectionString or ConnectionStrings:luciadb.");
                    options.UseNpgsql(connectionString);
                });
                break;

            case DataProviderType.MongoDB:
            default:
                // MongoDB is the default — no EF Core registration needed.
                // MongoDB services are registered by the individual host Program.cs files
                // using the existing Aspire MongoDB client integration.
                break;
        }

        return builder;
    }

    /// <summary>
    /// Returns true if the configured provider is NOT MongoDB (i.e., uses EF Core).
    /// Used by host Program.cs to skip MongoDB-specific registrations.
    /// </summary>
    public static bool IsEfCoreProvider()
    {
        var providerType = DataProviderOptions.ParseFromEnvironment();
        return providerType is DataProviderType.Sqlite or DataProviderType.PostgreSql;
    }

    /// <summary>
    /// Returns the configured <see cref="DataProviderType"/>.
    /// </summary>
    public static DataProviderType GetDataProviderType()
    {
        return DataProviderOptions.ParseFromEnvironment();
    }

    private static void AddEfCoreProvider(
        IHostApplicationBuilder builder,
        Action<DbContextOptionsBuilder> configureDb)
    {
        // Register the DbContext factory (thread-safe — creates a new DbContext per operation)
        builder.Services.AddDbContextFactory<LuciaDbContext>(configureDb);

        // Ensure database is created on startup
        builder.Services.AddHostedService<DatabaseInitializer>();

        // Register EF Core repository implementations as singletons (matching MongoDB lifetime).
        // Each repository uses IDbContextFactory to create/dispose a DbContext per operation.

        // Config database repos
        builder.Services.AddSingleton<IApiKeyService, EfApiKeyService>();
        builder.Services.AddSingleton<IModelProviderRepository, EfModelProviderRepository>();
        builder.Services.AddSingleton<IAgentDefinitionRepository, EfAgentDefinitionRepository>();
        builder.Services.AddSingleton<IPluginManagementRepository, EfPluginManagementRepository>();
        builder.Services.AddSingleton<IPresenceSensorRepository, EfPresenceSensorRepository>();

        // Task database repos
        builder.Services.AddSingleton<ITaskArchiveStore, EfTaskArchiveStore>();
        builder.Services.AddSingleton<IScheduledTaskRepository, EfScheduledTaskRepository>();
        builder.Services.AddSingleton<IAlarmClockRepository, EfAlarmClockRepository>();

        // Trace database repos
        builder.Services.AddSingleton<ITraceRepository, EfTraceRepository>();

        // Configuration provider — add EF-backed config source
        builder.Configuration.AddEfConfiguration(opts => configureDb(opts));
    }
}
