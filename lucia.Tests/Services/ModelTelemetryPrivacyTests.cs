using System.Reflection;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FakeItEasy;

namespace lucia.Tests.Services;

public sealed class ModelTelemetryPrivacyTests
{
    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.OpenRouter)]
    [InlineData(ProviderType.LlamaCpp)]
    [InlineData(ProviderType.AzureOpenAI)]
    [InlineData(ProviderType.AzureAIInference)]
    [InlineData(ProviderType.Ollama)]
    [InlineData(ProviderType.Anthropic)]
    [InlineData(ProviderType.GoogleGemini)]
    public void Chat_DefaultsToOneContentPrivateTelemetryWrapper(ProviderType type)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var resolver = new ModelProviderResolver(new StubHttpClientFactory(), NullLogger<ModelProviderResolver>.Instance, services);
        using var client = resolver.CreateClient(Provider(type));
        var wrappers = new List<OpenTelemetryChatClient>();
        var current = client;
        while (current is DelegatingChatClient wrapper)
        {
            if (wrapper is OpenTelemetryChatClient telemetry) wrappers.Add(telemetry);
            current = (IChatClient)typeof(DelegatingChatClient)
                .GetProperty("InnerClient", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(wrapper)!;
        }

        Assert.False(Assert.Single(wrappers).EnableSensitiveData);
    }

    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.OpenRouter)]
    [InlineData(ProviderType.LlamaCpp)]
    [InlineData(ProviderType.AzureOpenAI)]
    [InlineData(ProviderType.AzureAIInference)]
    [InlineData(ProviderType.Ollama)]
    [InlineData(ProviderType.GoogleGemini)]
    public void Embeddings_DefaultToOneContentPrivateTelemetryWrapper(ProviderType type)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var resolver = new ModelProviderResolver(new StubHttpClientFactory(), NullLogger<ModelProviderResolver>.Instance, services);
        using var generator = resolver.CreateEmbeddingGenerator(Provider(type));
        var wrappers = new List<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>();
        var current = generator;
        while (current is DelegatingEmbeddingGenerator<string, Embedding<float>> wrapper)
        {
            if (wrapper is OpenTelemetryEmbeddingGenerator<string, Embedding<float>> telemetry) wrappers.Add(telemetry);
            current = (IEmbeddingGenerator<string, Embedding<float>>)typeof(DelegatingEmbeddingGenerator<string, Embedding<float>>)
                .GetProperty("InnerGenerator", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(wrapper)!;
        }

        Assert.False(Assert.Single(wrappers).EnableSensitiveData);
    }

    private static ModelProvider Provider(ProviderType type) => new()
    {
        Id = "synthetic", Name = "Synthetic", ProviderType = type,
        Endpoint = "https://models.invalid/v1", ModelName = "synthetic-model",
        Auth = new ModelAuthConfig { ApiKey = "synthetic-key", AuthType = "api-key" }
    };

    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.OpenRouter)]
    [InlineData(ProviderType.LlamaCpp)]
    [InlineData(ProviderType.AzureOpenAI)]
    [InlineData(ProviderType.AzureAIInference)]
    [InlineData(ProviderType.Ollama)]
    [InlineData(ProviderType.Anthropic)]
    [InlineData(ProviderType.GoogleGemini)]
    public void Chat_ContentCaptureRequiresExplicitOptIn(ProviderType type)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var options = A.Fake<IOptionsMonitor<ModelTelemetryOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new ModelTelemetryOptions { IncludeModelContent = true });
        var resolver = new ModelProviderResolver(new StubHttpClientFactory(), NullLogger<ModelProviderResolver>.Instance, services, options);
        using var client = resolver.CreateClient(Provider(type));
        Assert.True(client.GetService<OpenTelemetryChatClient>()!.EnableSensitiveData);
    }

    [Theory]
    [InlineData(ProviderType.OpenAI)]
    [InlineData(ProviderType.OpenRouter)]
    [InlineData(ProviderType.LlamaCpp)]
    [InlineData(ProviderType.AzureOpenAI)]
    [InlineData(ProviderType.AzureAIInference)]
    [InlineData(ProviderType.Ollama)]
    [InlineData(ProviderType.GoogleGemini)]
    public void Embeddings_ContentCaptureRequiresExplicitOptIn(ProviderType type)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var options = A.Fake<IOptionsMonitor<ModelTelemetryOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new ModelTelemetryOptions { IncludeModelContent = true });
        var resolver = new ModelProviderResolver(new StubHttpClientFactory(), NullLogger<ModelProviderResolver>.Instance, services, options);
        using var generator = resolver.CreateEmbeddingGenerator(Provider(type));
        Assert.True(generator.GetService<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>()!.EnableSensitiveData);
    }
}
