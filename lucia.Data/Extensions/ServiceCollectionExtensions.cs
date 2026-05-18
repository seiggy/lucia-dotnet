using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.CommandTracing;
using lucia.Agents.DataStores;
using lucia.Agents.Services;
using lucia.Agents.Training;
using lucia.Data.InMemory;
using lucia.Data.PostgreSQL;
using lucia.Data.Sqlite;
using lucia.TimerAgent.ScheduledTasks;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using InMemoryTaskStore = lucia.Data.InMemory.InMemoryTaskStore;

namespace lucia.Data.Extensions;

/// <summary>
/// Extension methods for registering lightweight and relational data providers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers InMemory cache services (replacing Redis).
    /// </summary>
    public static IHostApplicationBuilder AddInMemoryCacheProviders(
        this IHostApplicationBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ISessionCacheService, lucia.Data.InMemory.InMemorySessionCacheService>();
        builder.Services.AddSingleton<IDeviceCacheService, lucia.Data.InMemory.InMemoryDeviceCacheService>();
        builder.Services.AddSingleton<IPromptCacheService, lucia.Data.InMemory.InMemoryPromptCacheService>();

        // Task store + task ID index (wraps with ArchivingTaskStore decorator when available)
        builder.Services.AddSingleton<InMemoryTaskStore>();
        builder.Services.AddSingleton<ITaskStore>(sp =>
        {
            var inMemoryStore = sp.GetRequiredService<InMemoryTaskStore>();
            var archive = sp.GetService<ITaskArchiveStore>();
            if (archive is not null)
            {
                var logger = sp.GetRequiredService<ILogger<ArchivingTaskStore>>();
                return new ArchivingTaskStore(inMemoryStore, archive, logger);
            }
            return inMemoryStore;
        });
        builder.Services.AddSingleton<ITaskIdIndex>(sp => sp.GetRequiredService<InMemoryTaskStore>());

        // Entity location service (in-memory, no Redis needed)
        builder.Services.AddSingleton<IEntityLocationService, lucia.Data.InMemory.InMemoryEntityLocationService>();
        builder.Services.AddSingleton<IMemoryStore, lucia.Data.InMemory.InMemoryMemoryStore>();
        AddMemorySupportServices(builder.Services);

        return builder;
    }

    /// <summary>
    /// Registers SQLite repository implementations (replacing MongoDB).
    /// </summary>
    public static IHostApplicationBuilder AddSqliteStoreProviders(
        this IHostApplicationBuilder builder)
    {
        var options = new DataProviderOptions();
        builder.Configuration.GetSection(DataProviderOptions.SectionName).Bind(options);

        // SQLite connection factory (skip if already registered by the host)
        builder.Services.TryAddSingleton(new SqliteConnectionFactory(options.SqlitePath));

        // Schema migration (registered as itself for direct resolution + as IHostedService)
        builder.Services.AddSingleton<SqliteMigrationRunner>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SqliteMigrationRunner>());

        // Config repositories
        builder.Services.AddSingleton<IConfigStoreWriter, SqliteConfigStoreWriter>();
        builder.Services.AddSingleton<IModelProviderRepository, SqliteModelProviderRepository>();
        builder.Services.AddSingleton<IAgentDefinitionRepository, SqliteAgentDefinitionRepository>();
        builder.Services.AddSingleton<IPresenceSensorRepository, SqlitePresenceSensorRepository>();
        builder.Services.AddSingleton<IPluginManagementRepository, SqlitePluginManagementRepository>();
        builder.Services.AddSingleton<IApiKeyService, SqliteApiKeyService>();

        // Data repositories
        builder.Services.AddSingleton<ITaskArchiveStore, SqliteTaskArchiveStore>();

        // Wyoming stores
        builder.Services.AddSingleton<ISpeakerProfileStore, SqliteSpeakerProfileStore>();
        builder.Services.AddSingleton<ITranscriptStore, SqliteTranscriptStore>();
        builder.Services.AddSingleton<IModelPreferenceStore, SqliteModelPreferenceStore>();
        builder.Services.AddSingleton<IMemoryStore, SqliteMemoryStore>();
        AddMemorySupportServices(builder.Services);

        return builder;
    }

    /// <summary>
    /// Registers PostgreSQL repository implementations.
    /// </summary>
    public static IHostApplicationBuilder AddPostgresStoreProviders(
        this IHostApplicationBuilder builder)
    {
        var options = new DataProviderOptions();
        builder.Configuration.GetSection(DataProviderOptions.SectionName).Bind(options);

        builder.Services.TryAddSingleton<PostgresConnectionFactory>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = options.PostgresConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = configuration.GetConnectionString("luciadb") ?? string.Empty;
            }

            return new PostgresConnectionFactory(connectionString);
        });

        builder.Services.AddSingleton<PostgresMigrationRunner>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PostgresMigrationRunner>());

        builder.Services.AddSingleton<IConfigStoreWriter, PostgresConfigStoreWriter>();
        builder.Services.AddSingleton<IModelProviderRepository, PostgresModelProviderRepository>();
        builder.Services.AddSingleton<IAgentDefinitionRepository, PostgresAgentDefinitionRepository>();
        builder.Services.AddSingleton<IPresenceSensorRepository, PostgresPresenceSensorRepository>();
        builder.Services.AddSingleton<IPluginManagementRepository, PostgresPluginManagementRepository>();
        builder.Services.AddSingleton<IApiKeyService, PostgresApiKeyService>();

        builder.Services.AddSingleton<ITaskArchiveStore, PostgresTaskArchiveStore>();
        builder.Services.AddSingleton<ITraceRepository, InMemoryTraceRepository>();
        builder.Services.AddSingleton<ICommandTraceRepository, InMemoryCommandTraceRepository>();
        builder.Services.AddSingleton<IScheduledTaskRepository, InMemoryScheduledTaskRepository>();
        builder.Services.AddSingleton<IAlarmClockRepository, InMemoryAlarmClockRepository>();

        builder.Services.AddSingleton<ISpeakerProfileStore, PostgresSpeakerProfileStore>();
        builder.Services.AddSingleton<ITranscriptStore, PostgresTranscriptStore>();
        builder.Services.AddSingleton<IModelPreferenceStore, PostgresModelPreferenceStore>();
        builder.Services.AddSingleton<IMemoryStore, PostgresMemoryStore>();
        AddMemorySupportServices(builder.Services);

        return builder;
    }

    private static void AddMemorySupportServices(IServiceCollection services)
    {
        services.TryAddSingleton<ChatHistoryProvider>();
        services.TryAddSingleton<UserContextProvider>();
    }
}
