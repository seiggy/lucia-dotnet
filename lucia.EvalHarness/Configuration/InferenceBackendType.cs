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
    OpenAICompat
}
