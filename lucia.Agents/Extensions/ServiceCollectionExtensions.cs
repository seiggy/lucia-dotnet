using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using lucia.Agents.Abstractions;
using lucia.Agents.Registry;
using lucia.Agents.Integration;
using lucia.Agents.Orchestration;
using lucia.Agents.Skills;
using lucia.Agents.Agents;
using lucia.Agents.Services;
using lucia.Agents.Training;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using lucia.HomeAssistant.Services;
using A2A;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using lucia.HomeAssistant.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using lucia.Agents.GitHubCopilot;
using lucia.Agents.Providers;

namespace lucia.Agents.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the Lucia multi-agent system to the service collection
    /// </summary>
    public static void AddLuciaAgents(
        this IHostApplicationBuilder builder)
    {
        builder.Services.AddHttpClient<IHomeAssistantClient, HomeAssistantClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 60);

            // Only set BaseAddress if fully configured (URL + token).
            // During wizard flow, these are empty at DI time and will be set
            // per-request via EnsureHttpClientConfigured() once the wizard saves config.
            if (!string.IsNullOrWhiteSpace(options.BaseUrl) && !string.IsNullOrWhiteSpace(options.AccessToken))
            {
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {options.AccessToken}");
            }
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var options = sp.GetRequiredService<IOptions<HomeAssistantOptions>>().Value;
            var handler = new HttpClientHandler();

            if (!options.ValidateSSL)
            {
                handler.ServerCertificateCustomValidationCallback =
                    (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) => true;
            }

            return handler;
        });

        // Register core services
        builder.Services.AddSingleton<IAgentRegistry, LocalAgentRegistry>();

        // Register Redis using Aspire client integration
        builder.AddRedisClient(connectionName: "redis");

        // Register Redis task store (T037) with archiving decorator
        builder.Services.AddSingleton<RedisTaskStore>();
        builder.Services.AddSingleton<ITaskStore>(sp =>
        {
            var redisStore = sp.GetRequiredService<RedisTaskStore>();
            var archive = sp.GetRequiredService<ITaskArchiveStore>();
            var logger = sp.GetRequiredService<ILogger<ArchivingTaskStore>>();
            return new ArchivingTaskStore(redisStore, archive, logger);
        });

        // Register Redis device cache service
        builder.Services.AddSingleton<IDeviceCacheService, RedisDeviceCacheService>();

        // Register entity location service (shared singleton for floor/area/entity resolution)
        builder.Services.AddSingleton<IEntityLocationService, EntityLocationService>();
        builder.Services.AddSingleton<IEmbeddingSimilarityService, EmbeddingSimilarityService>();

        // Register presence detection service (auto-discovers sensors, maps to areas)
        builder.Services.AddSingleton<IPresenceSensorRepository, MongoPresenceSensorRepository>();
        builder.Services.AddSingleton<IPresenceDetectionService, PresenceDetectionService>();

        // Register A2A TaskManager (T037)
        builder.Services.AddSingleton<ITaskManager>(sp =>
        {
            var taskStore = sp.GetRequiredService<ITaskStore>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("task-callbacks");
            return new TaskManager(httpClient, taskStore);
        });

        builder.Services.Configure<RouterExecutorOptions>(
            builder.Configuration.GetSection("RouterExecutor")
        );

        builder.Services.Configure<AgentInvokerOptions>(
            builder.Configuration.GetSection("AgentInvoker")
        );

        builder.Services.Configure<ResultAggregatorOptions>(
            builder.Configuration.GetSection("ResultAggregator")
        );

        builder.Services.Configure<HomeAssistantOptions>(
            builder.Configuration.GetSection("HomeAssistant"));

        builder.Services.Configure<SessionCacheOptions>(
            builder.Configuration.GetSection("SessionCache"));

        // Register Redis session cache for multi-turn conversations
        builder.Services.AddSingleton<ISessionCacheService, RedisSessionCacheService>();

        builder.Services.AddSingleton(TimeProvider.System);

        // Register session factory for orchestrator
        builder.Services.AddSingleton<IAgentSessionFactory, InMemorySessionFactory>();

        // Register orchestrator components and engine
        builder.Services.AddSingleton<SessionManager>();
        builder.Services.AddSingleton<WorkflowFactory>();
        builder.Services.AddSingleton<LuciaEngine>();
        builder.Services.AddSingleton<OrchestratorAgent>();
        builder.Services.AddSingleton<ILuciaAgent>(sp => sp.GetRequiredService<OrchestratorAgent>());

        // Register agent skills and agents
        builder.Services.AddSingleton<LightControlSkill>();
        builder.Services.AddSingleton<LightAgent>();
        builder.Services.AddSingleton<ILuciaAgent>(sp => sp.GetRequiredService<LightAgent>());
        builder.Services.AddSingleton<GeneralAgent>();
        builder.Services.AddSingleton<ILuciaAgent>(sp => sp.GetRequiredService<GeneralAgent>());
        builder.Services.AddSingleton<ClimateControlSkill>();
        builder.Services.AddSingleton<FanControlSkill>();
        builder.Services.AddSingleton<ClimateAgent>();
        builder.Services.AddSingleton<ILuciaAgent>(sp => sp.GetRequiredService<ClimateAgent>());

        // Register agent initialization background service
        builder.Services.AddSingleton<AgentInitializationStatus>();
        builder.Services.AddHostedService<AgentInitializationService>();
        builder.Services.AddHealthChecks()
            .AddCheck<AgentInitializationHealthCheck>("agent-initialization", tags: ["ready"]);

        // Register MCP tool registry and dynamic agent system
        builder.Services.AddSingleton<IAgentDefinitionRepository, MongoAgentDefinitionRepository>();
        builder.Services.AddSingleton<IMcpToolRegistry, McpToolRegistry>();
        builder.Services.AddSingleton<IDynamicAgentProvider, DynamicAgentProvider>();
        builder.Services.AddSingleton<DynamicAgentLoader>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<DynamicAgentLoader>());

        // Register model provider system
        builder.Services.AddSingleton<IModelProviderRepository, MongoModelProviderRepository>();
        builder.Services.AddSingleton<IModelProviderResolver, ModelProviderResolver>();
        builder.Services.AddSingleton<CopilotConnectService>();
        builder.Services.AddSingleton<CopilotClientLifecycleService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CopilotClientLifecycleService>());

        // Register embedding provider resolver — skills use this to get IEmbeddingGenerator
        // from the MongoDB-backed model provider system instead of hardcoded connection strings.
        builder.Services.AddSingleton<IEmbeddingProviderResolver, EmbeddingProviderResolver>();

        // Register chat client resolver — built-in agents use this to resolve IChatClient
        // from their AgentDefinition's ModelConnectionName via the model provider system.
        builder.Services.AddSingleton<IChatClientResolver, ChatClientResolver>();

        // Factory that wraps IChatClient with tracing for tool-call SSE events
        builder.Services.AddSingleton<TracingChatClientFactory>();
    }

}
