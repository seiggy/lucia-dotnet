using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using lucia.Agents.Registry;
using lucia.Agents.Orchestration;
using lucia.Agents.Skills;
using lucia.Agents.Agents;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Azure;
using Azure.AI.Inference;
using lucia.HomeAssistant.Services;
using Microsoft.Agents.AI.Hosting;
using OllamaSharp;

namespace lucia.Agents.Extensions;

public static class ServiceCollectionExtensions
{

    public static ChatClientBuilder AddChatClient(this IHostApplicationBuilder builder,
        string connectionName)
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName);

        if (!ChatClientConnectionInfo.TryParse(connectionString, out var connectionInfo))
        {
            throw new InvalidOperationException($"Invalid or missing connection string for '{connectionName}', expectedFormat: Endpoint=endpoint;AccessKey=your_access_key;Model=model_name;Provider=ollama/openai/azureopenai;");
        }

        var chatClientBuilder = connectionInfo.Provider switch
        {
            ClientChatProvider.Ollama => builder.AddOllamaClient(connectionName, connectionInfo),
            ClientChatProvider.OpenAI => builder.AddOpenAIClient(connectionName, connectionInfo),
            ClientChatProvider.AzureOpenAI => builder.AddAzureOpenAIClient(connectionName)
                .AddChatClient(connectionInfo.SelectedModel),
            ClientChatProvider.AzureAIInference => builder.AddAzureInferenceClient(connectionName, connectionInfo),
            _ => throw new NotSupportedException($"The specified provider '{connectionInfo.Provider}' is not supported.")
        };

        if (connectionInfo.Provider == ClientChatProvider.AzureOpenAI)
        {
            builder.AddAzureOpenAIClient(connectionName)
                .AddEmbeddingGenerator("text-embedding-3-small");
        }

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
            throw new InvalidOperationException($"Invalid connection string: {cs}. Expected format: 'Endpoint=endpoint;AccessKey=your_access_key;Model=model_name;Provider=ollama/openai/azureopenai;'.");
        }

        var chatClientBuilder = connectionInfo.Provider switch
        {
            ClientChatProvider.Ollama => builder.AddKeyedOllamaClient(connectionName, connectionInfo),
            ClientChatProvider.OpenAI => builder.AddKeyedOpenAIClient(connectionName, connectionInfo),
            ClientChatProvider.AzureOpenAI => builder.AddKeyedAzureOpenAIClient(connectionName).AddKeyedChatClient(connectionName, connectionInfo.SelectedModel),
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
        builder.Services.AddSingleton<AgentRegistry, LocalAgentRegistry>();

        // Register A2A services
        builder.Services.AddHttpClient<IA2AClientService, A2AClientService>();

        // Register Redis using Aspire client integration
        builder.AddRedisClient(connectionName: "redis");

        builder.Services.AddOptions<RouterExecutorOptions>();
        builder.Services.AddOptions<AgentExecutorWrapperOptions>();
        builder.Services.AddOptions<ResultAggregatorOptions>();
        builder.Services.AddSingleton(TimeProvider.System);

        // Register orchestrator
        builder.Services.AddSingleton<LuciaOrchestrator>();

        // Register plugins as singletons (for caching)
        builder.Services.AddSingleton<LightControlSkill>();
        builder.Services.AddSingleton<MusicPlaybackSkill>();

        // Register agents
        builder.Services.AddSingleton<LightAgent>();
        builder.Services.AddSingleton<MusicAgent>();

        builder.AddAIAgent("light-agent", (sp, name) =>
        {
            var lightAgent = sp.GetRequiredService<LightAgent>();
            lightAgent.InitializeAsync().GetAwaiter().GetResult();
            return lightAgent.GetAIAgent();
        });

        builder.AddAIAgent("music-agent", (sp, name) =>
        {
            var lightAgent = sp.GetRequiredService<MusicAgent>();
            lightAgent.InitializeAsync().GetAwaiter().GetResult();
            return lightAgent.GetAIAgent();
        });

        // Register the agent initialization service
        builder.Services.AddSingleton<IHostedService, AgentInitializationService>();
    }
}
