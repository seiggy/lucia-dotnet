using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using lucia.Agents.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace lucia.Tests.Services;

/// <summary>
/// Tests for <see cref="ModelProviderResolver"/> — validates that each supported
/// provider type creates a valid IChatClient and that validation rules are enforced.
/// These tests verify client construction only (no live LLM calls).
/// </summary>
public sealed class ModelProviderResolverTests
{
    private readonly ModelProviderResolver _resolver;

    public ModelProviderResolverTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        var serviceProvider = services.BuildServiceProvider();

        _resolver = new ModelProviderResolver(
            new StubHttpClientFactory(),
            NullLogger<ModelProviderResolver>.Instance,
            serviceProvider);
    }

    #region Helpers

    private static ModelProvider MakeProvider(
        ProviderType type,
        string? endpoint = null,
        string model = "test-model",
        string? apiKey = "test-key",
        bool useDefaultCredentials = false)
    {
        return new ModelProvider
        {
            Id = $"test-{type.ToString().ToLowerInvariant()}",
            Name = $"Test {type}",
            ProviderType = type,
            Endpoint = endpoint,
            ModelName = model,
            Auth = new ModelAuthConfig
            {
                AuthType = apiKey is not null ? "api-key" : (useDefaultCredentials ? "azure-credential" : "none"),
                ApiKey = apiKey,
                UseDefaultCredentials = useDefaultCredentials,
            },
            Enabled = true,
        };
    }

    #endregion

    #region OpenAI

    [Fact]
    public void CreateClient_OpenAI_WithApiKey_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.OpenAI, model: "gpt-4o");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_OpenAI_WithCustomEndpoint_ReturnsClient()
    {
        // OpenRouter scenario
        var provider = MakeProvider(ProviderType.OpenAI,
            endpoint: "https://openrouter.ai/api/v1",
            model: "openai/gpt-4o");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_OpenAI_WithoutApiKey_UsesPlaceholder()
    {
        var provider = MakeProvider(ProviderType.OpenAI, apiKey: null);
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    #endregion

    #region Azure OpenAI

    [Fact]
    public void CreateClient_AzureOpenAI_WithApiKey_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.AzureOpenAI,
            endpoint: "https://myinstance.openai.azure.com",
            model: "gpt-4o-deployment");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_AzureOpenAI_WithDefaultCredentials_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.AzureOpenAI,
            endpoint: "https://myinstance.openai.azure.com",
            model: "gpt-4o-deployment",
            apiKey: null,
            useDefaultCredentials: true);
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_AzureOpenAI_WithoutEndpoint_Throws()
    {
        var provider = MakeProvider(ProviderType.AzureOpenAI, endpoint: null);
        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.CreateClient(provider));
        Assert.Contains("endpoint", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateClient_AzureOpenAI_WithoutAnyAuth_Throws()
    {
        var provider = MakeProvider(ProviderType.AzureOpenAI,
            endpoint: "https://myinstance.openai.azure.com",
            apiKey: null,
            useDefaultCredentials: false);
        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.CreateClient(provider));
        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Azure AI Inference

    [Fact]
    public void CreateClient_AzureAIInference_WithApiKey_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.AzureAIInference,
            endpoint: "https://models.inference.ai.azure.com",
            model: "Phi-3-mini-4k-instruct");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_AzureAIInference_WithDefaultCredentials_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.AzureAIInference,
            endpoint: "https://models.inference.ai.azure.com",
            model: "Phi-3-mini-4k-instruct",
            apiKey: null,
            useDefaultCredentials: true);
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_AzureAIInference_WithoutEndpoint_Throws()
    {
        var provider = MakeProvider(ProviderType.AzureAIInference, endpoint: null);
        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.CreateClient(provider));
        Assert.Contains("endpoint", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateClient_AzureAIInference_WithoutAnyAuth_Throws()
    {
        var provider = MakeProvider(ProviderType.AzureAIInference,
            endpoint: "https://models.inference.ai.azure.com",
            apiKey: null,
            useDefaultCredentials: false);
        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.CreateClient(provider));
        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Ollama

    [Fact]
    public void CreateClient_Ollama_WithEndpoint_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.Ollama,
            endpoint: "http://localhost:11434",
            model: "llama3.2:3b",
            apiKey: null);
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_Ollama_WithoutEndpoint_UsesDefault()
    {
        var provider = MakeProvider(ProviderType.Ollama,
            endpoint: null,
            model: "phi3:mini",
            apiKey: null);
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    #endregion

    #region Anthropic

    [Fact]
    public void CreateClient_Anthropic_WithApiKey_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.Anthropic,
            model: "claude-sonnet-4-20250514",
            apiKey: "sk-ant-test-key");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_Anthropic_WithoutApiKey_Throws()
    {
        var provider = MakeProvider(ProviderType.Anthropic, apiKey: null);
        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.CreateClient(provider));
        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Google Gemini

    [Fact]
    public void CreateClient_GoogleGemini_WithApiKey_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.GoogleGemini,
            model: "gemini-2.0-flash",
            apiKey: "AIza-test-key");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_GoogleGemini_WithCustomEndpoint_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.GoogleGemini,
            endpoint: "https://custom-gemini.example.com/v1/",
            model: "gemini-2.0-flash",
            apiKey: "AIza-test-key");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_GoogleGemini_WithoutApiKey_Throws()
    {
        var provider = MakeProvider(ProviderType.GoogleGemini, apiKey: null);
        var ex = Assert.Throws<InvalidOperationException>(() => _resolver.CreateClient(provider));
        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region GitHub Copilot

    [Fact]
    public void CreateClient_GitHubCopilot_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.GitHubCopilot,
            model: "claude-sonnet-4",
            apiKey: null);
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    [Fact]
    public void CreateClient_GitHubCopilot_WithToken_ReturnsClient()
    {
        var provider = MakeProvider(ProviderType.GitHubCopilot,
            model: "gpt-4o",
            apiKey: "ghp_test_token_12345");
        using var client = _resolver.CreateClient(provider);
        Assert.NotNull(client);
    }

    #endregion

    #region Unsupported / Edge cases

    [Fact]
    public void CreateClient_UnsupportedProviderType_Throws()
    {
        var provider = MakeProvider((ProviderType)999);
        Assert.Throws<NotSupportedException>(() => _resolver.CreateClient(provider));
    }

    [Fact]
    public async Task TestConnectionAsync_WithBadProvider_ReturnsFailure()
    {
        // Anthropic with a fake key will fail when actually sending a request
        var provider = MakeProvider(ProviderType.Anthropic,
            model: "claude-sonnet-4-20250514",
            apiKey: "sk-ant-fake-key");

        var result = await _resolver.TestConnectionAsync(provider);

        Assert.False(result.Success);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Embedding Generators

    [Fact]
    public void CreateEmbeddingGenerator_OpenAI_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.OpenAI, model: "text-embedding-3-small");
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_OpenAI_WithCustomEndpoint_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.OpenAI,
            endpoint: "https://custom-endpoint.example.com/v1",
            model: "text-embedding-3-small");
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_AzureOpenAI_WithApiKey_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.AzureOpenAI,
            endpoint: "https://myinstance.openai.azure.com",
            model: "text-embedding-ada-002");
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_AzureOpenAI_WithDefaultCredentials_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.AzureOpenAI,
            endpoint: "https://myinstance.openai.azure.com",
            model: "text-embedding-ada-002",
            apiKey: null,
            useDefaultCredentials: true);
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_AzureOpenAI_RequiresEndpoint()
    {
        var provider = MakeProvider(ProviderType.AzureOpenAI, model: "text-embedding-ada-002");
        Assert.Throws<InvalidOperationException>(() => _resolver.CreateEmbeddingGenerator(provider));
    }

    [Fact]
    public void CreateEmbeddingGenerator_AzureAIInference_WithApiKey_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.AzureAIInference,
            endpoint: "https://my-inference.inference.ai.azure.com",
            model: "text-embedding-3-small");
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_AzureAIInference_RequiresEndpoint()
    {
        var provider = MakeProvider(ProviderType.AzureAIInference, model: "embed-model");
        Assert.Throws<InvalidOperationException>(() => _resolver.CreateEmbeddingGenerator(provider));
    }

    [Fact]
    public void CreateEmbeddingGenerator_Ollama_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.Ollama,
            model: "nomic-embed-text",
            apiKey: null);
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_Ollama_WithCustomEndpoint_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.Ollama,
            endpoint: "http://gpu-server:11434",
            model: "nomic-embed-text",
            apiKey: null);
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_GoogleGemini_ReturnsGenerator()
    {
        var provider = MakeProvider(ProviderType.GoogleGemini,
            model: "text-embedding-004");
        var generator = _resolver.CreateEmbeddingGenerator(provider);
        Assert.NotNull(generator);
    }

    [Fact]
    public void CreateEmbeddingGenerator_Anthropic_ThrowsNotSupported()
    {
        var provider = MakeProvider(ProviderType.Anthropic, model: "any-model");
        Assert.Throws<NotSupportedException>(() => _resolver.CreateEmbeddingGenerator(provider));
    }

    [Fact]
    public void CreateEmbeddingGenerator_GitHubCopilot_ThrowsNotSupported()
    {
        var provider = MakeProvider(ProviderType.GitHubCopilot, model: "any-model");
        Assert.Throws<NotSupportedException>(() => _resolver.CreateEmbeddingGenerator(provider));
    }

    [Fact]
    public async Task TestEmbeddingConnection_WithFakeKey_ReturnsFailed()
    {
        var provider = MakeProvider(ProviderType.OpenAI, model: "text-embedding-3-small");
        var result = await _resolver.TestEmbeddingConnectionAsync(provider);
        Assert.False(result.Success);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
