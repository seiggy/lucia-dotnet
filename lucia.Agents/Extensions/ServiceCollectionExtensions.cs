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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure;
using Azure.AI.Inference;
using lucia.HomeAssistant.Services;
using OllamaSharp;
using A2A;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using lucia.HomeAssistant.Configuration;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace lucia.Agents.Extensions;

public static class ServiceCollectionExtensions
{
    [Obsolete("Use the Model Provider system (IEmbeddingProviderResolver) instead. This will be removed in a future release.")]
    public static void AddEmbeddingsClient(this IHostApplicationBuilder builder, string connectionName)
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName);
        if (!ChatClientConnectionInfo.TryParse(connectionString, out var connectionInfo))
        {
            throw new InvalidOperationException($"Invalid or missing connection string for '{connectionName}'. " +
                "Expected custom format: Endpoint=endpoint;AccessKey=your_access_key;Model=model_name;Provider=ollama/openai/azureopenai; " +
                "or Aspire AI Foundry format: Endpoint=https://xxx.cognitiveservices.azure.com/;Deployment=name");
        }

        Console.WriteLine($"[lucia] Embeddings client '{connectionName}': Provider={connectionInfo.Provider}, " +
            $"Endpoint={connectionInfo.Endpoint}, Model={connectionInfo.SelectedModel}, " +
            $"HasKey={connectionInfo.AccessKey is not null}");
        
        switch (connectionInfo.Provider)
        {
            case ClientChatProvider.OpenAI:
                builder.AddOpenAIClient(connectionName, settings =>
                {
                    settings.Endpoint = connectionInfo.Endpoint;
                    settings.Key = connectionInfo.AccessKey;
                })
                .AddEmbeddingGenerator(connectionInfo.SelectedModel);
                break;
            case ClientChatProvider.AzureOpenAI:
                builder.AddAzureOpenAIClient(connectionName, settings =>
                {
                    settings.Credential = new AzureCliCredential();
                })
                    .AddEmbeddingGenerator(connectionInfo.SelectedModel);
                break;
            case ClientChatProvider.AzureAIInference:
                builder.Services.AddEmbeddingGenerator(sp =>
                {
                    var credential = new AzureKeyCredential(connectionInfo.AccessKey!);
                    var client = new Azure.AI.Inference.EmbeddingsClient(connectionInfo.Endpoint, credential);
                    return client.AsIEmbeddingGenerator();
                });
                break;
            case ClientChatProvider.Ollama:
                builder.AddOllamaEmbeddings(connectionName, connectionInfo);
                break;
            case ClientChatProvider.ONNX:
                throw new NotImplementedException("ONNX doesn't support embeddings still. So we can't support it as a provider.");
            default:
                throw new InvalidOperationException("Unsupported provider for embeddings client.");
        }
    }

    public static ChatClientBuilder AddChatClient(this IHostApplicationBuilder builder,
        string connectionName)
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName);

        if (!ChatClientConnectionInfo.TryParse(connectionString, out var connectionInfo))
        {
            throw new InvalidOperationException($"Invalid or missing connection string for '{connectionName}'. " +
                "Expected custom format: Endpoint=endpoint;AccessKey=your_access_key;Model=model_name;Provider=ollama/openai/azureopenai; " +
                "or Aspire AI Foundry format: Endpoint=https://xxx.cognitiveservices.azure.com/;Deployment=name");
        }

        Console.WriteLine($"[lucia] Chat client '{connectionName}': Provider={connectionInfo.Provider}, " +
            $"Endpoint={connectionInfo.Endpoint}, Model={connectionInfo.SelectedModel}, " +
            $"HasKey={connectionInfo.AccessKey is not null}");

        var chatClientBuilder = connectionInfo.Provider switch
        {
            ClientChatProvider.Ollama => builder.AddOllamaClient(connectionName, connectionInfo),
            ClientChatProvider.OpenAI => builder.AddOpenAIClient(connectionName, connectionInfo),
            ClientChatProvider.AzureOpenAI => builder.AddAzureOpenAIClient(connectionName, settings =>
                {
                    settings.Credential = new AzureCliCredential();
                })
                .AddChatClient(connectionInfo.SelectedModel),
            ClientChatProvider.AzureAIInference => builder.AddAzureInferenceClient(connectionName, connectionInfo),
            ClientChatProvider.ONNX => throw new NotImplementedException("ONNX doesn't support function calling still. So we can't support it as a provider."),
            _ => throw new NotSupportedException($"The specified provider '{connectionInfo.Provider}' is not supported.")
        };

        chatClientBuilder.UseOpenTelemetry().UseLogging();

        builder.Services.AddOpenTelemetry().
            WithTracing(t =>
                t.AddSource("Experimental.Microsoft.Extensions.AI")
            );

        return chatClientBuilder;
    }

    private static ChatClientBuilder AddOpenAIClient(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        return builder.AddOpenAIClient(connectionName, settings =>
        {
            settings.Endpoint = connectionInfo.Endpoint;
            settings.Key = connectionInfo.AccessKey;
        })
        .AddChatClient(connectionInfo.SelectedModel);
    }

    private static ChatClientBuilder AddAzureInferenceClient(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        return builder.Services.AddChatClient(sp =>
        {
            var credential = new AzureKeyCredential(connectionInfo.AccessKey!);
            var client = new ChatCompletionsClient(connectionInfo.Endpoint, credential, new AzureAIInferenceClientOptions());
            return client.AsIChatClient(connectionInfo.SelectedModel);
        });
    }

    private static void AddOllamaEmbeddings(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        var httpKey = $"{connectionName}_http";
        builder.Services.AddHttpClient(httpKey, c =>
        {
            c.BaseAddress = connectionInfo.Endpoint;
        });

        builder.Services.AddEmbeddingGenerator(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpKey);
            return new OllamaApiClient(client, connectionInfo.SelectedModel);
        });
    }

    private static ChatClientBuilder AddOllamaClient(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        var httpKey = $"{connectionName}_http";

        builder.Services.AddHttpClient(httpKey, client =>
        {
            client.BaseAddress = connectionInfo.Endpoint;
        });

        return builder.Services.AddChatClient(sp =>
        {
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpKey);
            return new OllamaApiClient(client, connectionInfo.SelectedModel);
        });
    }

    public static ChatClientBuilder AddKeyedChatClient(this IHostApplicationBuilder builder, string connectionName)
    {
        var cs = builder.Configuration.GetConnectionString(connectionName);

        if (!ChatClientConnectionInfo.TryParse(cs, out var connectionInfo))
        {
            throw new InvalidOperationException($"Invalid connection string: {cs}. " +
                "Expected custom format: Endpoint=endpoint;AccessKey=your_access_key;Model=model_name;Provider=ollama/openai/azureopenai; " +
                "or Aspire AI Foundry format: Endpoint=https://xxx.cognitiveservices.azure.com/;Deployment=name");
        }

        var chatClientBuilder = connectionInfo.Provider switch
        {
            ClientChatProvider.Ollama => builder.AddKeyedOllamaClient(connectionName, connectionInfo),
            ClientChatProvider.OpenAI => builder.AddKeyedOpenAIClient(connectionName, connectionInfo),
            ClientChatProvider.AzureOpenAI => builder.AddKeyedAzureOpenAIChatClient(connectionName, connectionInfo),
            ClientChatProvider.AzureAIInference => builder.AddKeyedAzureInferenceClient(connectionName, connectionInfo),
            _ => throw new NotSupportedException($"Unsupported provider: {connectionInfo.Provider}")
        };

        // Add OpenTelemetry tracing for the ChatClient activity source
        chatClientBuilder.UseOpenTelemetry().UseLogging();

        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddSource("Experimental.Microsoft.Extensions.AI"));

        return chatClientBuilder;
    }

    private static ChatClientBuilder AddKeyedOpenAIClient(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        return builder.AddKeyedOpenAIClient(connectionName, settings =>
        {
            settings.Endpoint = connectionInfo.Endpoint;
            settings.Key = connectionInfo.AccessKey;
        })
        .AddKeyedChatClient(connectionName, connectionInfo.SelectedModel);
    }

    private static ChatClientBuilder AddKeyedAzureInferenceClient(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        return builder.Services.AddKeyedChatClient(connectionName, sp =>
        {
            var credential = new AzureKeyCredential(connectionInfo.AccessKey!);

            var client = new ChatCompletionsClient(connectionInfo.Endpoint, credential, new AzureAIInferenceClientOptions());

            return client.AsIChatClient(connectionInfo.SelectedModel);
        });
    }

    /// <summary>
    /// Registers a keyed IChatClient for an Azure OpenAI deployment by reusing the
    /// shared non-keyed OpenAIClient. This avoids calling AddKeyedAzureOpenAIClient
    /// which poisons the non-keyed AzureOpenAIClient registration with a null factory.
    /// </summary>
    private static ChatClientBuilder AddKeyedAzureOpenAIChatClient(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        return builder.Services.AddKeyedChatClient(connectionName, sp =>
        {
            var client = sp.GetRequiredService<OpenAIClient>();
            return client.GetChatClient(connectionInfo.SelectedModel).AsIChatClient();
        });
    }

    private static ChatClientBuilder AddKeyedOllamaClient(this IHostApplicationBuilder builder, string connectionName, ChatClientConnectionInfo connectionInfo)
    {
        var httpKey = $"{connectionName}_http";

        builder.Services.AddHttpClient(httpKey, c =>
        {
            c.BaseAddress = connectionInfo.Endpoint;
        });

        return builder.Services.AddKeyedChatClient(connectionName, sp =>
        {
            // Create a client for the Ollama API using the http client factory
            var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(httpKey);

            return new OllamaApiClient(client, connectionInfo.SelectedModel);
        });
    }

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

        // Register default keyed IChatClient forwarding for the orchestrator router.
        // This forwards to the unkeyed IChatClient so the router resolves successfully
        // even when no per-router model is explicitly configured.
        builder.Services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.RouterModel,
            (sp, _) => sp.GetRequiredService<IChatClient>());

        // Wrap the router's keyed IChatClient with AgentTracingChatClient
        WrapAgentChatClientWithTracing(builder.Services, OrchestratorServiceKeys.RouterModel, "orchestrator-router");

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
