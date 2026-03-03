using System.Text.Json.Serialization;

namespace lucia.AgentHost.Models;

internal sealed class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}