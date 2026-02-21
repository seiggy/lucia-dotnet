using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

public class ServiceCallResponse
{
    [JsonPropertyName("context")]
    public HomeAssistantContext Context { get; set; } = new();
}