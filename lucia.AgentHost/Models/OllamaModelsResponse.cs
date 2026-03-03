namespace lucia.AgentHost.Models;

/// <summary>
/// Response from listing Ollama models. Models are populated on success; Error is set on failure.
/// </summary>
public sealed class OllamaModelsResponse
{
    public List<string> Models { get; set; } = [];
    public string? Error { get; set; }
}