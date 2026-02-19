namespace lucia.Tests.Orchestration;

/// <summary>
/// Azure OpenAI / AI Foundry connection settings for evaluation tests.
/// </summary>
public sealed class AzureOpenAISettings
{
    /// <summary>
    /// The Azure OpenAI or AI Foundry endpoint URL
    /// (e.g., <c>https://your-resource.openai.azure.com/</c>).
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Optional API key. When null, <c>AzureCliCredential</c> is used for authentication.
    /// </summary>
    public string? ApiKey { get; set; }
}
