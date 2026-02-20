using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Service domain with available services.
/// </summary>
public sealed record ServiceDomainInfo
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("services")]
    public required Dictionary<string, object?> Services { get; init; }
}
