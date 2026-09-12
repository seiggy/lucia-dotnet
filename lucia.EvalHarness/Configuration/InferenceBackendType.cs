namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Identifies the API protocol used by an inference backend.
/// </summary>
public enum InferenceBackendType
{
    /// <summary>
    /// Native Ollama API (<c>/api/chat</c>) via OllamaSharp.
    /// </summary>
    Ollama,

    /// <summary>
    /// OpenAI-compatible API (<c>/v1/chat/completions</c>).
    /// Works with llama.cpp, vLLM, LM Studio, and similar servers.
    /// </summary>
    OpenAICompat,

    /// <summary>Foundry deployments supporting the Azure OpenAI v1 Responses API.</summary>
    AzureFoundry,

    /// <summary>OpenRouter's OpenAI-compatible chat API and model pricing catalog.</summary>
    OpenRouter
}
