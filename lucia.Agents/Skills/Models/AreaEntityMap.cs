using System.Text.Json.Serialization;

public class AreaEntityMap
{
    [JsonPropertyName("area")]
    public string Area { get; set; } = string.Empty;
    
    [JsonPropertyName("entities")]
    public List<string> Entities { get; set; } = new();
}