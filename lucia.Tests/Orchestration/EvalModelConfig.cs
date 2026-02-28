namespace lucia.Tests.Orchestration;

/// <summary>
/// Configuration for a single model deployment used in evaluation tests.
/// </summary>
public sealed class EvalModelConfig
{
    /// <summary>
    /// The model or deployment name (e.g., <c>gpt-4o</c> for Azure OpenAI,
    /// <c>llama3.2</c> for Ollama).
    /// </summary>
    public required string DeploymentName { get; set; }

    /// <summary>
    /// Optional temperature override for this model. When null, the model's default
    /// temperature is used. Set to <c>0</c> for deterministic outputs. Omit for
    /// reasoning models (o-series) that do not support custom temperature.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// The LLM provider to use for this model. Defaults to
    /// <see cref="EvalProviderType.AzureOpenAI"/> when <c>null</c>.
    /// </summary>
    public EvalProviderType? Provider { get; set; }

    /// <summary>
    /// Provider endpoint URL. Required for Ollama (default: <c>http://localhost:11434</c>)
    /// and OpenAI custom endpoints. When <c>null</c>, uses the provider's default endpoint
    /// or the shared AzureOpenAI endpoint from <see cref="EvalConfiguration"/>.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API key for the provider. Only needed for OpenAI and some custom endpoints.
    /// For Azure OpenAI, the shared key from <see cref="AzureOpenAISettings"/> is used instead.
    /// </summary>
    public string? ApiKey { get; set; }
}
