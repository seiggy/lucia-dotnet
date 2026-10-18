using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Auth;
using lucia.Agents.CommandTracing;
using lucia.Agents.DataStores;
using lucia.Agents.Services;
using lucia.Agents.Training;
using lucia.Data.InMemory;
using lucia.Data.PostgreSQL;
using lucia.Data.Redis;
using lucia.Data.Sqlite;
using lucia.Data.Telemetry;
using lucia.TimerAgent.ScheduledTasks;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Models;
using lucia.Wyoming.Telemetry;
using AIContextProvider = Microsoft.Agents.AI.AIContextProvider;
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
    /// Registers the Redis cache providers: session, device and prompt caches, the task store
    /// (with archiving), the shared key-value cache and the entity location service.
    /// </summary>
    public static IHostApplicationBuilder AddRedisCacheProviders(
        this IHostApplicationBuilder builder)
    {
        // Redis through the Aspire client integration
        builder.AddRedisClient(connectionName: "redis");

        builder.Services.AddSingleton<RedisTaskStore>();
        builder.Services.AddSingleton<ITaskIdIndex>(sp => sp.GetRequiredService<RedisTaskStore>());
        builder.Services.AddSingleton<ITaskStore>(sp => new ArchivingTaskStore(
            sp.GetRequiredService<RedisTaskStore>(),
            sp.GetRequiredService<ITaskArchiveStore>(),
            sp.GetRequiredService<ILogger<ArchivingTaskStore>>()));

        builder.Services.AddSingleton<IDeviceCacheService, RedisDeviceCacheService>();
        builder.Services.AddSingleton<ISessionCacheService, RedisSessionCacheService>();
        builder.Services.AddSingleton<IPromptCacheService, RedisPromptCacheService>();
        builder.Services.AddSingleton<IKeyValueCache, RedisKeyValueCache>();

        // Shared floor/area/entity resolution, persisted to Redis
        builder.Services.AddSingleton<ILocationSnapshotStore, RedisLocationSnapshotStore>();
        builder.Services.AddSingleton<IVisibilityConfigStore, RedisVisibilityConfigStore>();
        builder.Services.AddSingleton<IEntityLocationService, EntityLocationService>();

        return builder;
    }
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
        builder.Services.AddSingleton<IKeyValueCache, lucia.Data.InMemory.InMemoryKeyValueCache>();

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

        // Same entity location service as the Redis mode; the snapshot stays in this process and
        // visibility settings persist in the SQLite or PostgreSQL configuration store.
        builder.Services.AddSingleton<ILocationSnapshotStore, InProcessLocationSnapshotStore>();
        builder.Services.AddSingleton<IVisibilityConfigStore, ConfigStoreVisibilityConfigStore>();
        builder.Services.AddSingleton<IEntityLocationService, EntityLocationService>();
        // Only register in-memory IMemoryStore if no durable store has been registered yet
        builder.Services.TryAddSingleton<IMemoryStore, lucia.Data.InMemory.InMemoryMemoryStore>();
        AddMemorySupportServices(builder.Services);

        return builder;
    }

    /// <summary>
    /// Registers SQLite repository implementations with three keyed connection factories
    /// mirroring the PostgreSQL three-database pattern (luciaconfig, luciatraces, luciatasks).
    /// </summary>
    public static IHostApplicationBuilder AddSqliteStoreProviders(
        this IHostApplicationBuilder builder)
    {
        var options = new DataProviderOptions();
        builder.Configuration.GetSection(DataProviderOptions.SectionName).Bind(options);

        var basePath = options.SqlitePath;

        // Register three keyed SqliteConnectionFactory instances.
        builder.Services.AddKeyedSingleton<SqliteConnectionFactory>(SqliteDbNames.Config, (_, _) =>
            new SqliteConnectionFactory(SqliteDbNames.GetCompatiblePath(basePath, SqliteDbNames.Config)));
        builder.Services.AddKeyedSingleton<SqliteConnectionFactory>(SqliteDbNames.Traces, (_, _) =>
            new SqliteConnectionFactory(SqliteDbNames.GetCompatiblePath(basePath, SqliteDbNames.Traces)));
        builder.Services.AddKeyedSingleton<SqliteConnectionFactory>(SqliteDbNames.Tasks, (_, _) =>
            new SqliteConnectionFactory(SqliteDbNames.GetCompatiblePath(basePath, SqliteDbNames.Tasks)));

        // Non-keyed factory for backward compat (migration runner, config provider).
        builder.Services.TryAddSingleton<SqliteConnectionFactory>(sp =>
            sp.GetRequiredKeyedService<SqliteConnectionFactory>(SqliteDbNames.Config));

        // Schema migration (registered as itself for direct resolution + as IHostedService)
        builder.Services.AddSingleton(sp => new SqliteMigrationRunner(
            sp.GetRequiredKeyedService<SqliteConnectionFactory>(SqliteDbNames.Config),
            sp.GetRequiredKeyedService<SqliteConnectionFactory>(SqliteDbNames.Traces),
            sp.GetRequiredKeyedService<SqliteConnectionFactory>(SqliteDbNames.Tasks),
            sp.GetRequiredService<ILogger<SqliteMigrationRunner>>(),
            basePath));
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SqliteMigrationRunner>());

        // luciaconfig repositories
        builder.Services.AddSingleton<IConfigStoreWriter, SqliteConfigStoreWriter>();
        builder.Services.AddSingleton<IModelProviderRepository, SqliteModelProviderRepository>();
        builder.Services.AddSingleton<IAgentDefinitionRepository, SqliteAgentDefinitionRepository>();
        builder.Services.AddSingleton<IPresenceSensorRepository, SqlitePresenceSensorRepository>();
        builder.Services.AddSingleton<IPluginManagementRepository, SqlitePluginManagementRepository>();
        AddCachedApiKeyService<SqliteApiKeyService>(builder.Services);
        AddSpeakerProfileStore<SqliteSpeakerProfileStore>(builder.Services);
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
        AddDataStoreHealth(builder.Services, sp => new SqliteDataStoreHealthProbe(
            Keyed<SqliteConnectionFactory>(sp, SqliteDbNames.Config, SqliteDbNames.Traces, SqliteDbNames.Tasks),
            sp.GetRequiredService<ILogger<SqliteDataStoreHealthProbe>>()));

        return builder;
    }

    /// <summary>
    /// Registers PostgreSQL repository implementations with three keyed connection factories
    /// mirroring the three-database pattern (luciaconfig, luciatraces, luciatasks).
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
        AddCachedApiKeyService<PostgresApiKeyService>(builder.Services);
        AddSpeakerProfileStore<PostgresSpeakerProfileStore>(builder.Services);
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
        AddDataStoreHealth(builder.Services, sp => new PostgresDataStoreHealthProbe(
            Keyed<PostgresConnectionFactory>(sp, PostgresDbNames.Config, PostgresDbNames.Traces, PostgresDbNames.Tasks),
            sp.GetRequiredService<ILogger<PostgresDataStoreHealthProbe>>()));

        return builder;
    }

    /// <summary>
    /// Registers the provider's health probe and the monitor that publishes <c>lucia.datastore.*</c> gauges.
    /// </summary>
    private static void AddDataStoreHealth(
        IServiceCollection services,
        Func<IServiceProvider, IDataStoreHealthProbe> createProbe)
    {
        services.AddSingleton(createProbe);
        services.AddHostedService<DataStoreHealthMonitor>();
    }

    private static Dictionary<string, TFactory> Keyed<TFactory>(IServiceProvider services, params string[] names)
        where TFactory : notnull
        => names.ToDictionary(name => name, name => services.GetRequiredKeyedService<TFactory>(name));

    private static void AddMemorySupportServices(IServiceCollection services)
    {
        services.TryAddSingleton<ChatHistoryProvider>();
        services.TryAddSingleton<UserContextProvider>();
        services.TryAddSingleton<AIContextProvider>(sp => sp.GetRequiredService<UserContextProvider>());
    }

    /// <summary>
    /// Registers <typeparamref name="TService"/> behind <see cref="CachedApiKeyService"/> so key
    /// validation skips the database on every authenticated request. The cache lives in the
    /// configured <see cref="IKeyValueCache"/> (Redis, or in-memory when none is registered).
    /// </summary>
    private static void AddCachedApiKeyService<TService>(IServiceCollection services)
        where TService : class, IApiKeyService
    {
        AddKeyValueCacheFallback(services);
        services.AddSingleton<TService>();
        services.AddSingleton<IApiKeyService>(sp => new CachedApiKeyService(
            sp.GetRequiredService<TService>(),
            sp.GetRequiredService<IKeyValueCache>(),
            sp.GetRequiredService<ILogger<CachedApiKeyService>>()));
    }

    /// <summary>
    /// Registers <typeparamref name="TStore"/> behind <see cref="CachedSpeakerProfileStore"/> for fast
    /// enrolled-profile reads, using the configured <see cref="IKeyValueCache"/>.
    /// </summary>
    private static void AddSpeakerProfileStore<TStore>(IServiceCollection services)
        where TStore : class, ISpeakerProfileStore
    {
        AddKeyValueCacheFallback(services);
        services.AddSingleton<TStore>();
        services.AddSingleton<ISpeakerProfileStore>(sp => new CachedSpeakerProfileStore(
            sp.GetRequiredService<TStore>(),
            sp.GetRequiredService<IKeyValueCache>(),
            sp.GetRequiredService<ILogger<CachedSpeakerProfileStore>>()));
    }

    // Keeps a Redis registration made earlier; otherwise caches in process.
    private static void AddKeyValueCacheFallback(IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton<IKeyValueCache, InMemoryKeyValueCache>();
    }
}
