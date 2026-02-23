using System.ClientModel;
using Anthropic;
using Azure;
using Azure.AI.Inference;
using Azure.Identity;
using GitHub.Copilot.SDK;
using lucia.Agents.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;

namespace lucia.Agents.Mcp;

/// <summary>
/// Creates IChatClient instances from stored ModelProvider configurations.
/// Supports OpenAI, Azure OpenAI, Azure AI Inference, Ollama, Anthropic, Google Gemini,
/// and GitHub Copilot SDK.
/// </summary>
public sealed class ModelProviderFactory : IModelProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelProviderFactory> _logger;
    private readonly IServiceProvider _serviceProvider;

    public ModelProviderFactory(
        IHttpClientFactory httpClientFactory,
        ILogger<ModelProviderFactory> logger,
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
            ProviderType.GitHubCopilot => CreateGitHubCopilotClient(provider),
            _ => throw new NotSupportedException($"Provider type '{provider.ProviderType}' is not supported")
        };

        // Wrap with telemetry and logging
        return new ChatClientBuilder(inner)
            .UseOpenTelemetry()
            .UseLogging()
            .Build(_serviceProvider);
    }

    public async Task<ModelProviderTestResult> TestConnectionAsync(ModelProvider provider, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(provider);
            var response = await client.GetResponseAsync("Say hello in one word.", cancellationToken: ct);
            var text = response.Text?.Trim();
            return new ModelProviderTestResult(true, $"Connection successful. Response: {text}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model provider test failed for {ProviderId}", provider.Id);
            return new ModelProviderTestResult(false, $"Connection failed: {ex.Message}");
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
        return client.GetChatClient(provider.ModelName).AsIChatClient();
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
            return client.GetChatClient(provider.ModelName).AsIChatClient();
        }

        if (!string.IsNullOrWhiteSpace(provider.Auth.ApiKey))
        {
            var credential = new ApiKeyCredential(provider.Auth.ApiKey);
            var client = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, credential);
            return client.GetChatClient(provider.ModelName).AsIChatClient();
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
            return client.AsIChatClient(provider.ModelName);
        }

        if (!string.IsNullOrWhiteSpace(provider.Auth.ApiKey))
        {
            var credential = new AzureKeyCredential(provider.Auth.ApiKey);
            var client = new ChatCompletionsClient(endpoint, credential, new AzureAIInferenceClientOptions());
            return client.AsIChatClient(provider.ModelName);
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
        return client.AsIChatClient(provider.ModelName);
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
        return client.GetChatClient(provider.ModelName).AsIChatClient();
    }

    private static IChatClient CreateGitHubCopilotClient(ModelProvider provider)
    {
        // GitHub Copilot SDK wraps the copilot CLI process.
        var options = new CopilotClientOptions();

        // Pass the GitHub token if configured (stored as the API key)
        if (!string.IsNullOrWhiteSpace(provider.Auth.ApiKey))
        {
            options.GithubToken = provider.Auth.ApiKey;
        }

        return new CopilotChatClientAdapter(options, provider.ModelName);
    }
}
