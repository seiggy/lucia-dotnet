using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using lucia.Agents.Services;
using lucia.Tests.Orchestration;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// <see cref="IEmbeddingProviderResolver"/> backed by <see cref="EvalModelConfig"/> entries.
/// Creates real embedding generators for each configured provider. When no
/// <c>EmbeddingModels</c> are configured, falls back to <see cref="DeterministicEmbeddingGenerator"/>.
/// </summary>
internal sealed class EvalEmbeddingProviderResolver : IEmbeddingProviderResolver
{
    private readonly IReadOnlyList<EvalModelConfig> _models;
    private readonly EvalConfiguration _config;

    public EvalEmbeddingProviderResolver(EvalConfiguration config)
    {
        _config = config;
        _models = config.EmbeddingModels;
    }

    public Task<IEmbeddingGenerator<string, Embedding<float>>?> ResolveAsync(
        string? providerName = null,
        CancellationToken ct = default)
    {
        if (_models.Count == 0)
            return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(
                new DeterministicEmbeddingGenerator());

        var model = !string.IsNullOrWhiteSpace(providerName)
            ? _models.FirstOrDefault(m =>
                string.Equals(m.DeploymentName, providerName, StringComparison.OrdinalIgnoreCase))
              ?? _models[0]
            : _models[0];

        var generator = CreateEmbeddingGenerator(model);
        return Task.FromResult<IEmbeddingGenerator<string, Embedding<float>>?>(generator);
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(EvalModelConfig model)
    {
        var provider = model.Provider ?? EvalProviderType.AzureOpenAI;
        return provider switch
        {
            EvalProviderType.AzureOpenAI => CreateAzureOpenAIEmbeddingGenerator(model),
            EvalProviderType.Ollama => CreateOllamaEmbeddingGenerator(model),
            EvalProviderType.OpenAI => CreateOpenAIEmbeddingGenerator(model),
            _ => throw new InvalidOperationException($"Unsupported embedding provider: {provider}")
        };
    }

    private IEmbeddingGenerator<string, Embedding<float>> CreateAzureOpenAIEmbeddingGenerator(EvalModelConfig model)
    {
        var endpoint = model.Endpoint ?? _config.AzureOpenAI.Endpoint;
        var apiKey = model.ApiKey ?? _config.AzureOpenAI.ApiKey;

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Azure OpenAI embedding requires an endpoint");

        var client = !string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
            : new AzureOpenAIClient(new Uri(endpoint), new Azure.Identity.DefaultAzureCredential());

        return client.GetEmbeddingClient(model.DeploymentName).AsIEmbeddingGenerator();
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateOllamaEmbeddingGenerator(EvalModelConfig model)
    {
        var endpoint = model.Endpoint ?? "http://localhost:11434";
        var httpClient = new HttpClient { BaseAddress = new Uri(endpoint) };
        return new OllamaApiClient(httpClient, model.DeploymentName);
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateOpenAIEmbeddingGenerator(EvalModelConfig model)
    {
        var apiKey = model.ApiKey ?? "unused";
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(model.Endpoint))
            options.Endpoint = new Uri(model.Endpoint);

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        return client.GetEmbeddingClient(model.DeploymentName).AsIEmbeddingGenerator();
    }
}
