using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

public class ServiceCallRequest : Dictionary<string, object>
{
    // Home Assistant service calls expect data at the root level, not wrapped in service_data
    // This class now inherits from Dictionary to allow flexible service data
    
    [JsonPropertyName("entity_id")]
    public string? EntityId 
    { 
        get => TryGetValue("entity_id", out var val) ? val?.ToString() : null;
        set { if (value is not null) this["entity_id"] = value; else Remove("entity_id"); }
    }
}