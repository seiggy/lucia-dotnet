namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Azure OpenAI connection settings for the LLM-as-judge evaluator.
/// The judge model scores agent quality — it is NOT the model under test.
/// </summary>
public sealed class AzureOpenAIJudgeSettings
{
    /// <summary>
    /// Azure OpenAI resource endpoint (e.g., <c>https://your-resource.openai.azure.com/</c>).
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// API key for the Azure OpenAI resource.
    /// When empty, falls back to <c>AzureCliCredential</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Deployment name for the judge model (e.g., <c>gpt-4o</c>).
    /// </summary>
    public string JudgeDeployment { get; set; } = "gpt-4o";
}
