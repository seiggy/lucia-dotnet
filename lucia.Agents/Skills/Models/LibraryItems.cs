using System.Text.Json.Serialization;

namespace lucia.Agents.Skills.Models;

public sealed record LibraryItems
{
    [JsonPropertyName("media_type")]
    public required string MediaType { get; set; }
    
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }
    
    [JsonPropertyName("name")]
    public required string Name { get; set; }
}
