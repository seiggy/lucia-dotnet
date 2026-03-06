using System.Text.Json.Serialization;

namespace lucia.Agents.Configuration;

/// <summary>
/// Supported LLM provider types. Determines which SDK is used to create the IChatClient.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderType
{
    /// <summary>OpenAI API (or other generic OpenAI-compatible endpoints).</summary>
    OpenAI,

    /// <summary>OpenRouter API (OpenAI-compatible endpoint with OpenRouter model metadata).</summary>
    OpenRouter,

    /// <summary>Azure OpenAI Service.</summary>
    AzureOpenAI,

    /// <summary>Azure AI Inference (Azure AI Foundry).</summary>
    AzureAIInference,

    /// <summary>Ollama local model server.</summary>
    Ollama,

    /// <summary>Anthropic Claude API.</summary>
    Anthropic,

    /// <summary>Google Gemini API.</summary>
    GoogleGemini,

    /// <summary>GitHub Copilot SDK (requires copilot CLI installed and authenticated).</summary>
    GitHubCopilot
}
