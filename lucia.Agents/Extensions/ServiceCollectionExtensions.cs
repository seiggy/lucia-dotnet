using System;
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
using Azure;
using Azure.AI.Inference;
using lucia.HomeAssistant.Services;
using Microsoft.Agents.AI.Hosting;
using OllamaSharp;
using A2A;
using lucia.Agents.Configuration;
using lucia.HomeAssistant.Configuration;
using Azure.Identity;
using OpenAI;
using OpenAI.Embeddings;

namespace lucia.Agents.Extensions;

public static class ServiceCollectionExtensions
{
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
        builder.Services.AddTransient<IHomeAssistantClient, GeneratedHomeAssistantClient>();
        // Register core services
        builder.Services.AddSingleton<IAgentRegistry, LocalAgentRegistry>();

        // Register Redis using Aspire client integration
        builder.AddRedisClient(connectionName: "redis");

        // Register Redis task store (T037)
        builder.Services.AddSingleton<ITaskStore, RedisTaskStore>();

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

        builder.Services.Configure<AgentExecutorWrapperOptions>(
            builder.Configuration.GetSection("AgentExecutorWrapper")
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

        // Register orchestration executors
        builder.Services.AddSingleton<RouterExecutor>();
        builder.Services.AddSingleton<AgentExecutorWrapper>(sp =>
        {
            // AgentExecutorWrapper requires agentId - using factory pattern
            // Individual wrappers will be created as needed by LuciaOrchestrator
            throw new InvalidOperationException("AgentExecutorWrapper should not be resolved directly. It's created by LuciaOrchestrator.");
        });
        builder.Services.AddSingleton<ResultAggregatorExecutor>();

        // Register session factory for orchestrator
        builder.Services.AddSingleton<IAgentSessionFactory, InMemorySessionFactory>();

        // Register default keyed IChatClient forwardings for all agent model keys.
        // Each key forwards to the unkeyed IChatClient so agents resolve successfully
        // even when no per-agent model is explicitly configured.
        foreach (var key in OrchestratorServiceKeys.AllAgentModelKeys)
        {
            builder.Services.AddKeyedSingleton<IChatClient>(
                key,
                (sp, _) => sp.GetRequiredService<IChatClient>());
        }

        // Apply config-driven per-agent model overrides.
        // When an agent's AgentConfiguration specifies a ModelConnectionName, register
        // a real keyed IChatClient for that agent's service key, overriding the default
        // forwarding above.
        var agentConfigs = builder.Configuration.GetSection("Agents").Get<List<AgentConfiguration>>() ?? [];
        var overrideCount = 0;
        foreach (var agentConfig in agentConfigs)
        {
            if (string.IsNullOrWhiteSpace(agentConfig.ModelConnectionName))
            {
                Console.WriteLine(
                    $"[lucia] Agent '{agentConfig.AgentName}': No ModelConnectionName configured — using default model.");
                continue;
            }

            var serviceKey = ResolveAgentServiceKey(agentConfig.AgentName);
            if (serviceKey is null)
            {
                Console.WriteLine(
                    $"[lucia] WARNING: Agent '{agentConfig.AgentName}': Name not recognized — " +
                    $"cannot map to a keyed IChatClient. ModelConnectionName '{agentConfig.ModelConnectionName}' ignored.");
                continue;
            }

            // Verify the connection string exists before attempting registration
            var connectionString = builder.Configuration.GetConnectionString(agentConfig.ModelConnectionName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine(
                    $"[lucia] WARNING: Agent '{agentConfig.AgentName}': ModelConnectionName " +
                    $"'{agentConfig.ModelConnectionName}' specified but connection string " +
                    $"'ConnectionStrings:{agentConfig.ModelConnectionName}' not found — falling back to default model.");
                continue;
            }

            overrideCount++;

            // Register the keyed chat client from the named connection string.
            // This overwrites the default forwarding for this key.
            builder.AddKeyedChatClient(agentConfig.ModelConnectionName);

            // Re-register the agent's service key to resolve from the named connection
            builder.Services.AddKeyedSingleton<IChatClient>(
                serviceKey,
                (sp, _) => sp.GetRequiredKeyedService<IChatClient>(agentConfig.ModelConnectionName));

            Console.WriteLine(
                $"[lucia] Agent '{agentConfig.AgentName}': Model override applied — " +
                $"using connection '{agentConfig.ModelConnectionName}' (key: {serviceKey}).");
        }

        if (overrideCount == 0 && agentConfigs.Count > 0)
        {
            Console.WriteLine(
                $"[lucia] INFO: {agentConfigs.Count} agent(s) configured but none have ModelConnectionName set. " +
                "All agents will use the default model. Set Agents[].ModelConnectionName in appsettings.json " +
                "to assign per-agent models.");
        }

        // Register orchestrator and orchestrator agent
        builder.Services.AddSingleton<LuciaOrchestrator>();
        builder.Services.AddSingleton<OrchestratorAgent>();

        // Register agent skills and agents
        builder.Services.AddSingleton<LightControlSkill>();
        builder.Services.AddSingleton<LightAgent>();
        builder.Services.AddSingleton<ILuciaAgent>(sp => sp.GetRequiredService<LightAgent>());
        builder.Services.AddSingleton<GeneralAgent>();
        builder.Services.AddSingleton<ILuciaAgent>(sp => sp.GetRequiredService<GeneralAgent>());

        // Register agent initialization background service
        builder.Services.AddHostedService<AgentInitializationService>();

        // Wrap each agent's keyed IChatClient with AgentTracingChatClient
        // to capture full conversation traces (prompts, tool calls, results) per agent.
        WrapAgentChatClientWithTracing(builder.Services, OrchestratorServiceKeys.LightModel, "light-agent");
        WrapAgentChatClientWithTracing(builder.Services, OrchestratorServiceKeys.MusicModel, "music-agent");
        WrapAgentChatClientWithTracing(builder.Services, OrchestratorServiceKeys.GeneralModel, "general-assistant");
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

    /// <summary>
    /// Maps a configured agent name to its well-known keyed service key.
    /// Returns null if the agent name is not recognized.
    /// </summary>
    private static string? ResolveAgentServiceKey(string? agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            return null;

        return agentName.ToLowerInvariant() switch
        {
            "light-agent" or "lightagent" => OrchestratorServiceKeys.LightModel,
            "music-agent" or "musicagent" => OrchestratorServiceKeys.MusicModel,
            "general-assistant" or "generalagent" => OrchestratorServiceKeys.GeneralModel,
            "router" or "orchestrator" => OrchestratorServiceKeys.RouterModel,
            _ => null
        };
    }
}
