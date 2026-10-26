namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Ollama connection settings for model discovery and agent execution.
/// </summary>
public sealed class OllamaSettings
{
    /// <summary>Default requested context budget when a profile does not override it.</summary>
    public int? DefaultNumContext { get; set; }

    /// <summary>
    /// Base URL for the Ollama API. Defaults to <c>http://localhost:11434</c>.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Explicit provider-charge declaration used when no named backends are configured.</summary>
    public bool NoProviderCharge { get; set; }
}
