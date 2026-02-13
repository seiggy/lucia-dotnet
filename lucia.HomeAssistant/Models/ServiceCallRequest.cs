using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

public class ServiceCallRequest : Dictionary<string, object>
{
    // Home Assistant service calls expect data at the root level, not wrapped in service_data
    // This class now inherits from Dictionary to allow flexible service data
    
    [JsonIgnore]
    public string? EntityId 
    { 
        get => this.ContainsKey("entity_id") ? this["entity_id"]?.ToString() : null;
        set => this["entity_id"] = value;
    }
}