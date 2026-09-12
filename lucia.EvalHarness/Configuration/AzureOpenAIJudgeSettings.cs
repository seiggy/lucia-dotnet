namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Azure OpenAI connection settings for the LLM-as-judge evaluator.
/// The judge model scores agent quality — it is NOT the model under test.
/// </summary>
public sealed class AzureOpenAIJudgeSettings
{
    /// <summary>
    /// Azure OpenAI or Foundry resource URL, optionally ending in <c>/openai/v1/</c>.
    /// Foundry project URLs are not supported.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// API key for the Azure OpenAI resource.
    /// When empty, falls back to <c>AzureCliCredential</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Deployment name for the judge model, not the underlying model ID.
    /// </summary>
    public string JudgeDeployment { get; set; } = string.Empty;

    /// <summary>
    /// Use the Responses API for modern models. Set false for legacy Chat Completions deployments.
    /// Responses requests omit sampling overrides and disable stored output.
    /// </summary>
    public bool UseResponsesApi { get; set; } = true;
}
