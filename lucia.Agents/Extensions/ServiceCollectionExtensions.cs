using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using lucia.Agents.Registry;
using lucia.Agents.Orchestration;
using lucia.Agents.Plugins;
using lucia.Agents.Agents;
using lucia.Agents.Reducers;

namespace lucia.Agents.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the Lucia multi-agent system to the service collection
    /// </summary>
    public static IServiceCollection AddLuciaAgents(
        this IServiceCollection services,
        Func<IServiceProvider, Kernel> kernelFactory,
        Func<IServiceProvider, IChatHistoryReducer>? chatHistoryReducerFactory = null)
    {
        // Register core services
        services.AddSingleton<IAgentRegistry, AgentRegistry>();
        
        // Register the kernel
        services.AddSingleton(kernelFactory);
        
        // Register chat history reducer (default to summarizing reducer if not provided)
        if (chatHistoryReducerFactory != null)
        {
            services.AddSingleton(chatHistoryReducerFactory);
        }
        else
        {
            services.AddSingleton<IChatHistoryReducer>(serviceProvider =>
            {
                var kernel = serviceProvider.GetRequiredService<Kernel>();
                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var logger = serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SummarizingChatHistoryReducer>>();
                
                return new SummarizingChatHistoryReducer(chatService, logger);
            });
        }
        
        // Register orchestrator
        services.AddSingleton<LuciaOrchestrator>();
        
        // Register plugins as singletons (for caching)
        services.AddSingleton<LightControlPlugin>();
        
        // Register agents
        services.AddTransient<LightAgent>();
        
        // Register the agent initialization service
        services.AddSingleton<IHostedService, AgentInitializationService>();
        
        return services;
    }
    
    /// <summary>
    /// Add the Lucia multi-agent system with default Semantic Kernel configuration
    /// </summary>
    public static IServiceCollection AddLuciaAgents(
        this IServiceCollection services,
        string openAiApiKey = "no-key-provided",
        string chatModelId = "gpt-4o",
        string embeddingModelId = "text-embedding-3-small",
        int maxTokens = 8000)
    {
        services.AddOpenAIChatCompletion(chatModelId, openAiApiKey);
        services.AddOpenAIEmbeddingGenerator(modelId: embeddingModelId, apiKey: openAiApiKey);
        
        return services.AddLuciaAgents(serviceProvider =>
        {
            var kernelBuilder = Kernel.CreateBuilder();
            
            // Add chat completion service
            kernelBuilder.Services.AddOpenAIChatCompletion(chatModelId, openAiApiKey);
            
            // Add embedding service
            kernelBuilder.Services.AddOpenAIEmbeddingGenerator(modelId: embeddingModelId, apiKey: openAiApiKey);
            
            // Add logging
            kernelBuilder.Services.AddLogging();
            
            return kernelBuilder.Build();
        });
    }
}