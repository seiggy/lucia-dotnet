using System.Net;
using System.Text;
using FakeItEasy;
using lucia.AgentHost.Services;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services;

public sealed class ProviderModelCatalogServiceTests
{
    [Fact]
    public async Task ListModelsAsync_OpenRouterConnectionDetails_ReturnsToolCapableCatalogModels()
    {
        HttpRequestMessage? capturedRequest = null;
        var responseBody = """
            {
              "data": [
                { "id": "openai/gpt-4o", "supported_parameters": ["max_tokens", "tools", "tool_choice"] },
                { "id": "anthropic/claude-sonnet-4", "supported_parameters": ["tool_choice"] },
                { "id": "meta/llama-3.1", "supported_parameters": ["max_tokens"] }
              ]
            }
            """;

        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            })));
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);

        var result = await service.ListModelsAsync(
            ProviderType.OpenRouter,
            "https://openrouter.ai/api/v1",
            new ModelAuthConfig { AuthType = "api-key", ApiKey = "test-key" });

        Assert.Null(result.Error);
        Assert.Equal(["anthropic/claude-sonnet-4", "openai/gpt-4o"], result.Models);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://openrouter.ai/api/v1/models?supported_parameters=tools", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ListModelsAsync_OpenRouterConnectionDetails_InvalidEndpoint_ReturnsError()
    {
        var httpFactory = A.Fake<IHttpClientFactory>();
        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);

        var result = await service.ListModelsAsync(
            ProviderType.OpenRouter,
            "not-a-valid-endpoint",
            new ModelAuthConfig { AuthType = "api-key", ApiKey = "test-key" });

        Assert.NotNull(result.Error);
        Assert.Contains("Invalid OpenRouter endpoint URL", result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task ListModelsAsync_OpenRouterUnauthorized_ReturnsExplicitAuthError()
    {
        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);

        var result = await service.ListModelsAsync(
            ProviderType.OpenRouter,
            "https://openrouter.ai/api/v1",
            new ModelAuthConfig { AuthType = "api-key", ApiKey = "bad-key" });

        Assert.NotNull(result.Error);
        Assert.Contains("authentication failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task ListModelsAsync_OpenRouterNotFound_ReturnsExplicitEndpointError()
    {
        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);

        var result = await service.ListModelsAsync(
            ProviderType.OpenRouter,
            "https://openrouter.ai/api/v1/invalid",
            new ModelAuthConfig { AuthType = "api-key", ApiKey = "test-key" });

        Assert.NotNull(result.Error);
        Assert.Contains("endpoint", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task ListModelsAsync_OpenAiCompatibleEndpoint_ReturnsCatalogModels()
    {
        HttpRequestMessage? capturedRequest = null;
        var responseBody = """
            {
              "data": [
                { "id": "model-b" },
                { "id": "model-a" },
                { "id": "model-a" }
              ]
            }
            """;

        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            })));
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);
        var provider = BuildProvider(
            ProviderType.OpenAI,
            endpoint: "https://openrouter.ai/api/v1",
            apiKey: "test-key");

        var result = await service.ListModelsAsync(provider);

        Assert.Null(result.Error);
        Assert.Equal(["model-a", "model-b"], result.Models);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://openrouter.ai/api/v1/models", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("test-key", capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ListModelsAsync_OpenAiWithoutEndpoint_UsesDefaultV1ModelsUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "data": [] }""", Encoding.UTF8, "application/json")
                };
            })));
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);
        var provider = BuildProvider(ProviderType.OpenAI, endpoint: null, apiKey: "test-key");

        var result = await service.ListModelsAsync(provider);

        Assert.Null(result.Error);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://api.openai.com/v1/models", capturedRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListModelsAsync_OpenAiCompatibleUnauthorized_ReturnsExplicitAuthError()
    {
        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);
        var provider = BuildProvider(ProviderType.OpenAI, endpoint: "https://api.openai.com/v1", apiKey: "bad-key");

        var result = await service.ListModelsAsync(provider);

        Assert.NotNull(result.Error);
        Assert.Contains("Authentication failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task ListModelsAsync_OpenAiCompatibleNotFound_ReturnsExplicitEndpointError()
    {
        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);
        var provider = BuildProvider(ProviderType.OpenAI, endpoint: "https://api.openai.com/v1/invalid", apiKey: "test-key");

        var result = await service.ListModelsAsync(provider);

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Models);
    }

    [Fact]
    public async Task ListModelsAsync_Ollama_ReturnsSortedNames()
    {
        var responseBody = """
            {
              "models": [
                { "name": "llama3.1:8b" },
                { "name": "qwen2.5:14b" }
              ]
            }
            """;

        var httpFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpFactory.CreateClient("OllamaModels"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            })));
        A.CallTo(() => httpFactory.CreateClient("ProviderModelCatalog"))
            .Returns(new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);
        var provider = BuildProvider(ProviderType.Ollama, endpoint: "http://localhost:11434", apiKey: null);

        var result = await service.ListModelsAsync(provider);

        Assert.Null(result.Error);
        Assert.Equal(["llama3.1:8b", "qwen2.5:14b"], result.Models);
    }

    [Fact]
    public async Task ListModelsAsync_UnsupportedProviderType_ReturnsError()
    {
        var httpFactory = A.Fake<IHttpClientFactory>();
        var service = new ProviderModelCatalogService(httpFactory, NullLogger<ProviderModelCatalogService>.Instance);
        var provider = BuildProvider(ProviderType.Anthropic, endpoint: null, apiKey: "test-key");

        var result = await service.ListModelsAsync(provider);

        Assert.NotNull(result.Error);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Models);
    }

    private static ModelProvider BuildProvider(ProviderType providerType, string? endpoint, string? apiKey)
    {
        return new ModelProvider
        {
            Id = $"provider-{providerType.ToString().ToLowerInvariant()}",
            Name = $"Provider {providerType}",
            ProviderType = providerType,
            Purpose = ModelPurpose.Chat,
            Endpoint = endpoint,
            ModelName = string.Empty,
            Auth = new ModelAuthConfig
            {
                AuthType = apiKey is null ? "none" : "api-key",
                ApiKey = apiKey,
                UseDefaultCredentials = false
            },
            Enabled = true
        };
    }
}
