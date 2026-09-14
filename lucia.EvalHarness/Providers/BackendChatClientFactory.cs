using System.ClientModel;
using System.ClientModel.Primitives;
using lucia.Agents.Providers;
using lucia.EvalHarness.Configuration;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace lucia.EvalHarness.Providers;

/// <summary>
/// Creates an <see cref="IChatClient"/> for a given <see cref="InferenceBackend"/>.
/// Supports both native Ollama (OllamaSharp) and OpenAI-compatible endpoints
/// (llama.cpp, vLLM, LM Studio) via the OpenAI SDK.
/// </summary>
public static class BackendChatClientFactory
{
    /// <summary>
    /// Creates a chat client for the specified backend and model, wrapped with
    /// the given parameter profile for inference tuning.
    /// </summary>
    public static IChatClient CreateChatClient(
        InferenceBackend backend,
        string modelName,
        ModelParameterProfile profile)
    {
        return new ParameterInjectingChatClient(CreateChatClient(backend, modelName), profile);
    }

    public static IChatClient CreateChatClient(InferenceBackend backend, string modelName) =>
        CreateRawClient(backend, modelName, null);

    internal static IChatClient CreateRawClient(InferenceBackend backend, string modelName, HttpClient? httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        var inner = backend.Type switch
        {
            InferenceBackendType.Ollama => CreateOllamaClient(backend.Endpoint, modelName),
            InferenceBackendType.OpenAICompat => CreateOpenAICompatClient(backend, modelName, httpClient),
            InferenceBackendType.OpenRouter => CreateOpenAICompatClient(backend, modelName, httpClient),
            InferenceBackendType.AzureFoundry => JudgeClientFactory.Create(new AzureOpenAIJudgeSettings
            {
                Endpoint = backend.Endpoint,
                ApiKey = backend.ApiKey,
                JudgeDeployment = modelName
            }, httpClient) ?? throw new InvalidOperationException("Foundry inference configuration is missing."),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), $"Unsupported backend type: {backend.Type}")
        };
        return new InferenceCostChatClient(inner, backend, modelName);
    }

    private static IChatClient CreateOllamaClient(string endpoint, string modelName)
    {
        var uri = new Uri(endpoint.TrimEnd('/'));
        return new OllamaApiClient(uri, modelName);
    }

    private static IChatClient CreateOpenAICompatClient(InferenceBackend backend, string modelName, HttpClient? httpClient)
    {
        if (backend.Type == InferenceBackendType.OpenRouter && string.IsNullOrWhiteSpace(backend.ApiKey))
        {
            throw new InvalidOperationException($"Backend '{backend.Name}' requires an OpenRouter API key.");
        }

        var baseUri = LlamaCppEndpoint.Normalize(backend.Endpoint);
        var credential = new ApiKeyCredential(string.IsNullOrWhiteSpace(backend.ApiKey) ? "not-needed" : backend.ApiKey);
        var options = new OpenAIClientOptions { Endpoint = baseUri };
        if (httpClient is not null)
        {
            options.Transport = new HttpClientPipelineTransport(httpClient);
        }
        var openAiClient = new OpenAIClient(credential, options);

        return openAiClient.GetChatClient(modelName).AsIChatClient();
    }
}
