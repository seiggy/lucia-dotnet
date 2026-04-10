using System.ClientModel;
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
        IChatClient inner = backend.Type switch
        {
            InferenceBackendType.Ollama => CreateOllamaClient(backend.Endpoint, modelName),
            InferenceBackendType.OpenAICompat => CreateOpenAICompatClient(backend.Endpoint, modelName),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), $"Unsupported backend type: {backend.Type}")
        };

        return new ParameterInjectingChatClient(inner, profile);
    }

    private static IChatClient CreateOllamaClient(string endpoint, string modelName)
    {
        var uri = new Uri(endpoint.TrimEnd('/'));
        return new OllamaApiClient(uri, modelName);
    }

    private static IChatClient CreateOpenAICompatClient(string endpoint, string modelName)
    {
        // OpenAI SDK pointed at a local OpenAI-compatible server.
        // llama.cpp, vLLM, and LM Studio all serve /v1/chat/completions.
        var baseUri = new Uri(endpoint.TrimEnd('/') + "/v1");
        var credential = new ApiKeyCredential("not-needed");
        var options = new OpenAIClientOptions { Endpoint = baseUri };
        var openAiClient = new OpenAIClient(credential, options);

        return openAiClient.GetChatClient(modelName).AsIChatClient();
    }
}
