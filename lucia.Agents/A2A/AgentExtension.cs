using System.Text.Json.Serialization;

namespace lucia.Agents.A2A;

public class AgentExtension
{
    public required string Uri { get; set; }
    public string? Description { get; set; }
    public bool? Required { get; set; }
    
    [JsonPropertyName("params")]
    public Dictionary<string, object>? Parameters { get; set; }
}