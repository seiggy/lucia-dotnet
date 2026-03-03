using System.Text.Json.Serialization;

namespace lucia.AgentHost.Models;

/// <summary>
/// Response from GET /api/tags on an Ollama instance.
/// </summary>
internal sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo> Models { get; set; } = [];
}