using System.Text.Json;
using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Configuration;

/// <summary>
/// Pre-configured JSON serializer options for Home Assistant API.
/// Uses snake_case naming to match HA JSON conventions.
/// </summary>
internal static class HomeAssistantJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
}
