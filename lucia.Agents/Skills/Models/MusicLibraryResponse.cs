using System.Text.Json.Serialization;

namespace lucia.Agents.Skills.Models;

public sealed record MusicLibraryResponse
{
    [JsonPropertyName("service_response")]
    public required MusicLibraryServiceResponse ServiceResponse { get; set; } 
}