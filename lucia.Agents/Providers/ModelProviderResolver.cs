using System.ClientModel;
using Anthropic;
using Azure;
using Azure.AI.Inference;
using Azure.Identity;
using GitHub.Copilot.SDK;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.GitHubCopilot;
using lucia.Agents.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;

namespace lucia.Agents.Providers;

/// <summary>
/// Creates IChatClient and IEmbeddingGenerator instances from stored ModelProvider configurations.
/// Supports OpenAI, Azure OpenAI, Azure AI Inference, Ollama, Anthropic, Google Gemini,
/// and GitHub Copilot SDK.
/// </summary>
public sealed class ModelProviderResolver : IModelProviderResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelProviderResolver> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ModelProviderResolver(
        IHttpClientFactory httpClientFactory,
        ILogger<ModelProviderResolver> logger,
        IServiceProvider serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public IChatClient CreateClient(ModelProvider provider)
    {
        _logger.LogDebug("Creating IChatClient for provider {ProviderId} (type={ProviderType}, model={Model})",
            provider.Id, provider.ProviderType, provider.ModelName);

        IChatClient inner = provider.ProviderType switch
        {
            ProviderType.OpenAI => CreateOpenAIClient(provider),
            ProviderType.AzureOpenAI => CreateAzureOpenAIClient(provider),
            ProviderType.AzureAIInference => CreateAzureAIInferenceClient(provider),
            ProviderType.Ollama => CreateOllamaClient(provider),
            ProviderType.Anthropic => CreateAnthropicClient(provider),
            ProviderType.GoogleGemini => CreateGeminiClient(provider),
            ProviderType.GitHubCopilot => throw new InvalidOperationException(
                "GitHub Copilot providers produce AIAgent directly. Use CreateAIAgentAsync() instead of CreateClient()."),
            _ => throw new NotSupportedException($"Provider type '{provider.ProviderType}' is not supported")
        };

        // Wrap with telemetry and logging
        return new ChatClientBuilder(inner)
            .UseOpenTelemetry()
            .UseLogging()
            .Build(_serviceProvider);
    }

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(ModelProvider provider)
    {
        _logger.LogDebug("Creating IEmbeddingGenerator for provider {ProviderId} (type={ProviderType}, model={Model})",
            provider.Id, provider.ProviderType, provider.ModelName);

        return provider.ProviderType switch
        {
            ProviderType.OpenAI => CreateOpenAIEmbeddingGenerator(provider),
            ProviderType.AzureOpenAI => CreateAzureOpenAIEmbeddingGenerator(provider),
            ProviderType.AzureAIInference => CreateAzureAIInferenceEmbeddingGenerator(provider),
            ProviderType.Ollama => CreateOllamaEmbeddingGenerator(provider),
            ProviderType.GoogleGemini => CreateGeminiEmbeddingGenerator(provider),
            _ => throw new NotSupportedException(
                $"Provider type '{provider.ProviderType}' does not support embedding generation. " +
                "Supported types: OpenAI, AzureOpenAI, AzureAIInference, Ollama, GoogleGemini.")
        };
    }

    public async Task<ModelProviderTestResult> TestConnectionAsync(ModelProvider provider, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(provider);
            var response = await client.GetResponseAsync("Say hello in one word.", cancellationToken: ct).ConfigureAwait(false);
            var text = response.Text?.Trim();
            return new ModelProviderTestResult(true, $"Connection successful. Response: {text}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model provider test failed for {ProviderId}", provider.Id);
            return new ModelProviderTestResult(false, $"Connection failed: {ex.Message}");
        }
    }

    public async Task<ModelProviderTestResult> TestEmbeddingConnectionAsync(ModelProvider provider, CancellationToken ct = default)
    {
        try
        {
            var generator = CreateEmbeddingGenerator(provider);
            var result = await generator.GenerateAsync(["hello world"], cancellationToken: ct).ConfigureAwait(false);
            var dims = result.FirstOrDefault()?.Vector.Length ?? 0;
            return new ModelProviderTestResult(true, $"Embedding successful. Dimensions: {dims}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding provider test failed for {ProviderId}", provider.Id);
            return new ModelProviderTestResult(false, $"Embedding test failed: {ex.Message}");
        }
    }

    private static IChatClient CreateOpenAIClient(ModelProvider provider)
    {
        // Works for OpenAI, OpenRouter, GitHub Models, and any OpenAI-compatible endpoint
        var apiKey = provider.Auth.ApiKey ?? "unused";
        var credential = new ApiKeyCredential(apiKey);

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            options.Endpoint = new Uri(provider.Endpoint);
        }

        var client = new OpenAIClient(credential, options);
        return client.GetChatClient(provider.ModelName)
            .AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                cfg.EnableSensitiveData = true)
            .Build();
    }

    private static IChatClient CreateAzureOpenAIClient(ModelProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Endpoint))
            throw new InvalidOperationException("Azure OpenAI provider requires an endpoint URL");

        var endpoint = new Uri(provider.Endpoint);

        if (provider.Auth is { UseDefaultCredentials: true })
        {
            var credential = new DefaultAzureCredential();
            var client = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, credential);
            return client.GetChatClient(provider.ModelName).AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        if (!string.IsNullOrWhiteSpace(provider.Auth.ApiKey))
        {
            var credential = new ApiKeyCredential(provider.Auth.ApiKey);
            var client = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, credential);
            return client.GetChatClient(provider.ModelName).AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        throw new InvalidOperationException("Azure OpenAI provider requires either an API key or Azure credentials");
    }

    private static IChatClient CreateAzureAIInferenceClient(ModelProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Endpoint))
            throw new InvalidOperationException("Azure AI Inference provider requires an endpoint URL");

        var endpoint = new Uri(provider.Endpoint);

        if (provider.Auth is { UseDefaultCredentials: true })
        {
            var credential = new DefaultAzureCredential();
            var client = new ChatCompletionsClient(endpoint, credential, new AzureAIInferenceClientOptions());
            return client.AsIChatClient(provider.ModelName)
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        if (!string.IsNullOrWhiteSpace(provider.Auth.ApiKey))
        {
            var credential = new AzureKeyCredential(provider.Auth.ApiKey);
            var client = new ChatCompletionsClient(endpoint, credential, new AzureAIInferenceClientOptions());
            return client.AsIChatClient(provider.ModelName)
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        throw new InvalidOperationException("Azure AI Inference provider requires either an API key or Azure credentials");
    }

    private IChatClient CreateOllamaClient(ModelProvider provider)
    {
        var httpClient = _httpClientFactory.CreateClient($"ollama_{provider.Id}");
        if (!string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            httpClient.BaseAddress = new Uri(provider.Endpoint);
        }
        else
        {
            httpClient.BaseAddress = new Uri("http://localhost:11434");
        }

        return new OllamaApiClient(httpClient, provider.ModelName);
    }

    private static IChatClient CreateAnthropicClient(ModelProvider provider)
    {
        var apiKey = provider.Auth.ApiKey
            ?? throw new InvalidOperationException("Anthropic provider requires an API key");

        var client = new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = apiKey });
        return client.AsIChatClient(provider.ModelName)
            .AsBuilder()
            .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                cfg.EnableSensitiveData = true)
            .Build();
    }

    private static IChatClient CreateGeminiClient(ModelProvider provider)
    {
        // Google Gemini uses OpenAI-compatible endpoint via generativelanguage.googleapis.com
        var apiKey = provider.Auth.ApiKey
            ?? throw new InvalidOperationException("Google Gemini provider requires an API key");

        var endpoint = !string.IsNullOrWhiteSpace(provider.Endpoint)
            ? provider.Endpoint
            : "https://generativelanguage.googleapis.com/v1beta/openai/";

        var credential = new ApiKeyCredential(apiKey);
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var client = new OpenAIClient(credential, options);
        return client.GetChatClient(provider.ModelName).AsIChatClient()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                cfg.EnableSensitiveData = true)
            .Build();
    }

    public async Task<AIAgent?> CreateAIAgentAsync(ModelProvider provider, CancellationToken ct = default)
    {
        if (provider.ProviderType != ProviderType.GitHubCopilot)
        {
            return null;
        }

        // Resolve optionally — not all hosts register the Copilot lifecycle service
        var copilotLifecycle = _serviceProvider.GetService<CopilotClientLifecycleService>();
        if (copilotLifecycle is null)
        {
            throw new InvalidOperationException(
                "GitHub Copilot provider is configured but CopilotClientLifecycleService is not registered. " +
                "Ensure AddLuciaAgents() is called on the host.");
        }

        await copilotLifecycle.EnsureStartedAsync(provider, ct).ConfigureAwait(false);

        var client = copilotLifecycle.Client
            ?? throw new InvalidOperationException("GitHub Copilot CLI is not running. Check logs for startup errors.");

        var sessionConfig = new SessionConfig
        {
            Model = provider.ModelName
        };

        _logger.LogInformation("Creating GitHubCopilotAgent for provider {ProviderId} (model={Model})",
            provider.Id, provider.ModelName);

        return client.AsAIAgent(
            sessionConfig,
            ownsClient: false,
            id: provider.Id,
            name: provider.Name,
            description: $"GitHub Copilot agent using {provider.ModelName}");
    }

    #region Embedding generators

    private static IEmbeddingGenerator<string, Embedding<float>> CreateOpenAIEmbeddingGenerator(ModelProvider provider)
    {
        var apiKey = provider.Auth.ApiKey ?? "unused";
        var credential = new ApiKeyCredential(apiKey);

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            options.Endpoint = new Uri(provider.Endpoint);
        }

        var client = new OpenAIClient(credential, options);
        return client.GetEmbeddingClient(provider.ModelName).AsIEmbeddingGenerator()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                cfg.EnableSensitiveData = true)
            .Build();
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateAzureOpenAIEmbeddingGenerator(ModelProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Endpoint))
            throw new InvalidOperationException("Azure OpenAI embedding provider requires an endpoint URL");

        var endpoint = new Uri(provider.Endpoint);

        if (provider.Auth is { UseDefaultCredentials: true })
        {
            // DefaultAzureCredential requires AzureOpenAIClient for token-based auth
            var credential = new DefaultAzureCredential();
            var client = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, credential);
            return client.GetEmbeddingClient(provider.ModelName).AsIEmbeddingGenerator()
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        if (!string.IsNullOrWhiteSpace(provider.Auth.ApiKey))
        {
            var credential = new AzureKeyCredential(provider.Auth.ApiKey);
            var client = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, credential);
            return client.GetEmbeddingClient(provider.ModelName).AsIEmbeddingGenerator()
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        throw new InvalidOperationException("Azure OpenAI embedding provider requires either an API key or Azure credentials");
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateAzureAIInferenceEmbeddingGenerator(ModelProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.Endpoint))
            throw new InvalidOperationException("Azure AI Inference embedding provider requires an endpoint URL");

        var endpoint = new Uri(provider.Endpoint);

        if (provider.Auth is { UseDefaultCredentials: true })
        {
            var credential = new DefaultAzureCredential();
            var client = new EmbeddingsClient(endpoint, credential, new AzureAIInferenceClientOptions());
            return client.AsIEmbeddingGenerator(provider.ModelName)
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        if (!string.IsNullOrWhiteSpace(provider.Auth.ApiKey))
        {
            var credential = new AzureKeyCredential(provider.Auth.ApiKey);
            var client = new EmbeddingsClient(endpoint, credential, new AzureAIInferenceClientOptions());
            return client.AsIEmbeddingGenerator(provider.ModelName)
                .AsBuilder()
                .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                    cfg.EnableSensitiveData = true)
                .Build();
        }

        throw new InvalidOperationException("Azure AI Inference embedding provider requires either an API key or Azure credentials");
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateOllamaEmbeddingGenerator(ModelProvider provider)
    {
        var httpClient = _httpClientFactory.CreateClient($"ollama_embed_{provider.Id}");
        httpClient.BaseAddress = !string.IsNullOrWhiteSpace(provider.Endpoint)
            ? new Uri(provider.Endpoint)
            : new Uri("http://localhost:11434");

        return new OllamaApiClient(httpClient, provider.ModelName);
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateGeminiEmbeddingGenerator(ModelProvider provider)
    {
        // Google Gemini embedding via OpenAI-compatible endpoint
        var apiKey = provider.Auth.ApiKey
            ?? throw new InvalidOperationException("Google Gemini embedding provider requires an API key");

        var endpoint = !string.IsNullOrWhiteSpace(provider.Endpoint)
            ? provider.Endpoint
            : "https://generativelanguage.googleapis.com/v1beta/openai/";

        var credential = new ApiKeyCredential(apiKey);
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        var client = new OpenAIClient(credential, options);
        return client.GetEmbeddingClient(provider.ModelName).AsIEmbeddingGenerator()
            .AsBuilder()
            .UseOpenTelemetry(sourceName: provider.Name, configure: (cfg) =>
                cfg.EnableSensitiveData = true)
            .Build();
    }

    #endregion
}
