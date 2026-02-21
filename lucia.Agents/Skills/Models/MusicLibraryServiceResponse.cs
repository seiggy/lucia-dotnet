using System.Text.Json.Serialization;

namespace lucia.Agents.Skills.Models;

public sealed record MusicLibraryServiceResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<LibraryItems> Items { get; set; }
}
