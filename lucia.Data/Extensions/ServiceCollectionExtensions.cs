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
        // Only register in-memory IMemoryStore if no durable store has been registered yet
        builder.Services.TryAddSingleton<IMemoryStore, lucia.Data.InMemory.InMemoryMemoryStore>();
        AddMemorySupportServices(builder.Services);

        return builder;
    }

    /// <summary>
    /// Registers SQLite repository implementations with three keyed connection factories
    /// mirroring the MongoDB/PostgreSQL three-database pattern (luciaconfig, luciatraces, luciatasks).
    /// </summary>
    public static IHostApplicationBuilder AddSqliteStoreProviders(
        this IHostApplicationBuilder builder)
    {
        var options = new DataProviderOptions();
        builder.Configuration.GetSection(DataProviderOptions.SectionName).Bind(options);

        // Derive three database file paths from the configured base path.
        // e.g. "./data/lucia.db" → "./data/lucia-config.db", "./data/lucia-traces.db", "./data/lucia-tasks.db"
        var basePath = options.SqlitePath;
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);

        var configPath = Path.Combine(dir, $"{name}-config{ext}");
        var tracesPath = Path.Combine(dir, $"{name}-traces{ext}");
        var tasksPath = Path.Combine(dir, $"{name}-tasks{ext}");

        // Register three keyed SqliteConnectionFactory instances.
        builder.Services.AddKeyedSingleton<SqliteConnectionFactory>(SqliteDbNames.Config, (_, _) =>
            new SqliteConnectionFactory(configPath));
        builder.Services.AddKeyedSingleton<SqliteConnectionFactory>(SqliteDbNames.Traces, (_, _) =>
            new SqliteConnectionFactory(tracesPath));
        builder.Services.AddKeyedSingleton<SqliteConnectionFactory>(SqliteDbNames.Tasks, (_, _) =>
            new SqliteConnectionFactory(tasksPath));

        // Non-keyed factory for backward compat (migration runner, config provider).
        builder.Services.TryAddSingleton<SqliteConnectionFactory>(sp =>
            sp.GetRequiredKeyedService<SqliteConnectionFactory>(SqliteDbNames.Config));

        // Schema migration (registered as itself for direct resolution + as IHostedService)
        builder.Services.AddSingleton<SqliteMigrationRunner>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SqliteMigrationRunner>());

        // luciaconfig repositories
        builder.Services.AddSingleton<IConfigStoreWriter, SqliteConfigStoreWriter>();
        builder.Services.AddSingleton<IModelProviderRepository, SqliteModelProviderRepository>();
        builder.Services.AddSingleton<IAgentDefinitionRepository, SqliteAgentDefinitionRepository>();
        builder.Services.AddSingleton<IPresenceSensorRepository, SqlitePresenceSensorRepository>();
        builder.Services.AddSingleton<IPluginManagementRepository, SqlitePluginManagementRepository>();
        builder.Services.AddSingleton<IApiKeyService, SqliteApiKeyService>();
        builder.Services.AddSingleton<ISpeakerProfileStore, SqliteSpeakerProfileStore>();
        builder.Services.AddSingleton<ITranscriptStore, SqliteTranscriptStore>();
        builder.Services.AddSingleton<IModelPreferenceStore, SqliteModelPreferenceStore>();
        builder.Services.AddSingleton<IMemoryStore, SqliteMemoryStore>();

        // luciatraces repositories
        builder.Services.AddSingleton<ITraceRepository, SqliteTraceRepository>();
        builder.Services.AddSingleton<ICommandTraceRepository, SqliteCommandTraceRepository>();

        // luciatasks repositories
        builder.Services.AddSingleton<ITaskArchiveStore, SqliteTaskArchiveStore>();
        builder.Services.AddSingleton<IScheduledTaskRepository, SqliteScheduledTaskRepository>();
        builder.Services.AddSingleton<IAlarmClockRepository, SqliteAlarmClockRepository>();

        AddMemorySupportServices(builder.Services);

        return builder;
    }

    /// <summary>
    /// Registers PostgreSQL repository implementations with three keyed connection factories
    /// mirroring the MongoDB three-database pattern (luciaconfig, luciatraces, luciatasks).
    /// </summary>
    public static IHostApplicationBuilder AddPostgresStoreProviders(
        this IHostApplicationBuilder builder)
    {
        // Register three keyed PostgresConnectionFactory instances (one per database).
        // Connection strings come from Aspire-injected ConnectionStrings section.
        builder.Services.AddKeyedSingleton<PostgresConnectionFactory>(PostgresDbNames.Config, (sp, _) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connStr = config.GetConnectionString(PostgresDbNames.Config) ?? string.Empty;
            return new PostgresConnectionFactory(connStr);
        });

        builder.Services.AddKeyedSingleton<PostgresConnectionFactory>(PostgresDbNames.Traces, (sp, _) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connStr = config.GetConnectionString(PostgresDbNames.Traces) ?? string.Empty;
            return new PostgresConnectionFactory(connStr);
        });

        builder.Services.AddKeyedSingleton<PostgresConnectionFactory>(PostgresDbNames.Tasks, (sp, _) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connStr = config.GetConnectionString(PostgresDbNames.Tasks) ?? string.Empty;
            return new PostgresConnectionFactory(connStr);
        });

        // Also register a non-keyed factory pointing at config DB for backward compat
        // (used by PostgresMigrationRunner and PostgresConfigurationProvider).
        builder.Services.TryAddSingleton<PostgresConnectionFactory>(sp =>
            sp.GetRequiredKeyedService<PostgresConnectionFactory>(PostgresDbNames.Config));

        builder.Services.AddSingleton<PostgresMigrationRunner>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<PostgresMigrationRunner>());

        // luciaconfig repositories
        builder.Services.AddSingleton<IConfigStoreWriter, PostgresConfigStoreWriter>();
        builder.Services.AddSingleton<IModelProviderRepository, PostgresModelProviderRepository>();
        builder.Services.AddSingleton<IAgentDefinitionRepository, PostgresAgentDefinitionRepository>();
        builder.Services.AddSingleton<IPresenceSensorRepository, PostgresPresenceSensorRepository>();
        builder.Services.AddSingleton<IPluginManagementRepository, PostgresPluginManagementRepository>();
        builder.Services.AddSingleton<IApiKeyService, PostgresApiKeyService>();
        builder.Services.AddSingleton<ISpeakerProfileStore, PostgresSpeakerProfileStore>();
        builder.Services.AddSingleton<ITranscriptStore, PostgresTranscriptStore>();
        builder.Services.AddSingleton<IModelPreferenceStore, PostgresModelPreferenceStore>();
        builder.Services.AddSingleton<IMemoryStore, PostgresMemoryStore>();

        // luciatraces repositories
        builder.Services.AddSingleton<ITraceRepository, PostgresTraceRepository>();
        builder.Services.AddSingleton<ICommandTraceRepository, PostgresCommandTraceRepository>();

        // luciatasks repositories
        builder.Services.AddSingleton<ITaskArchiveStore, PostgresTaskArchiveStore>();
        builder.Services.AddSingleton<IScheduledTaskRepository, PostgresScheduledTaskRepository>();
        builder.Services.AddSingleton<IAlarmClockRepository, PostgresAlarmClockRepository>();

        AddMemorySupportServices(builder.Services);

        return builder;
    }

    private static void AddMemorySupportServices(IServiceCollection services)
    {
        services.TryAddSingleton<ChatHistoryProvider>();
        services.TryAddSingleton<UserContextProvider>();
    }
}
