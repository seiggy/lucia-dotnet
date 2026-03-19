using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.DataStores;
using lucia.Data.Sqlite;
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
/// Extension methods for registering lightweight (InMemory/SQLite) data providers.
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

        return builder;
    }
}
