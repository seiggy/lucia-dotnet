namespace lucia.Tests.Orchestration;

/// <summary>
/// Supported LLM provider types for evaluation tests.
/// When <c>null</c> in <see cref="EvalModelConfig"/>, defaults to <see cref="AzureOpenAI"/>.
/// </summary>
public enum EvalProviderType
{
    /// <summary>Azure OpenAI Service. Uses the shared AzureOpenAI endpoint from <see cref="EvalConfiguration"/>.</summary>
    AzureOpenAI,

    /// <summary>Ollama local model server (default endpoint: <c>http://localhost:11434</c>).</summary>
    Ollama,

    /// <summary>OpenAI API (or any OpenAI-compatible endpoint).</summary>
    OpenAI
}
