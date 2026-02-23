using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lucia.Agents.Configuration;

/// <summary>
/// Supported LLM provider types. Determines which SDK is used to create the IChatClient.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderType
{
    /// <summary>OpenAI API (or any OpenAI-compatible endpoint like OpenRouter, GitHub Models).</summary>
    OpenAI,

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
