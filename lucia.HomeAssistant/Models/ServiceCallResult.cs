using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Response from a service call with return_response.
/// </summary>
public sealed record ServiceCallResult
{
    [JsonPropertyName("changed_states")]
    public required IReadOnlyList<HomeAssistantState> ChangedStates { get; init; }

    [JsonPropertyName("service_response")]
    public Dictionary<string, object>? ServiceResponse { get; init; }
}
