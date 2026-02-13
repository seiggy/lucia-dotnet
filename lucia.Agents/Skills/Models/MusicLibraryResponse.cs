using System.Text.Json.Serialization;

namespace lucia.Agents.Skills.Models;

public sealed record MusicLibraryResponse
{
    [JsonPropertyName("service_response")]
    public MusicLibraryServiceResponse ServiceResponse { get; set; } 
}

public sealed record MusicLibraryServiceResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<LibraryItems> Items { get; set; }
}

public sealed record LibraryItems
{
    [JsonPropertyName("media_type")]
    public string MediaType { get; set; }
    
    [JsonPropertyName("uri")]
    public string Uri { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; }
}