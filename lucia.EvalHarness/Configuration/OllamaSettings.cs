namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Ollama connection settings for model discovery and agent execution.
/// </summary>
public sealed class OllamaSettings
{
    /// <summary>
    /// Base URL for the Ollama API. Defaults to <c>http://localhost:11434</c>.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";
}
