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
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/'));
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.AccessToken}");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
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
    }

    /// <summary>
    /// Wraps a keyed IChatClient registration with <see cref="AgentTracingChatClient"/>
    /// by removing the existing registration and re-adding one that decorates the original.
    /// </summary>
    public static void WrapAgentChatClientWithTracing(IServiceCollection services, string serviceKey, string agentId)
    {
        // Find the existing keyed registration for this service key
        var existing = services.LastOrDefault(d =>
            d.ServiceType == typeof(IChatClient) &&
            d.IsKeyedService &&
            string.Equals(d.ServiceKey?.ToString(), serviceKey, StringComparison.Ordinal));

        if (existing is null)
            return;

        // Capture the original factory
        var originalFactory = existing.KeyedImplementationFactory;
        var originalInstance = existing.KeyedImplementationInstance;

        // Remove the original registration
        services.Remove(existing);

        // Re-register with tracing wrapper
        services.AddKeyedSingleton<IChatClient>(serviceKey, (sp, key) =>
        {
            IChatClient inner;
            if (originalFactory is not null)
                inner = (IChatClient)originalFactory(sp, key);
            else if (originalInstance is IChatClient instance)
                inner = instance;
            else
                inner = sp.GetRequiredService<IChatClient>();

            var repository = sp.GetRequiredService<ITraceRepository>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AgentTracingChatClient>>();
            return new AgentTracingChatClient(inner, agentId, repository, logger);
        });
    }

}
