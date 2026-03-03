using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Per-entity voice assistant exposure flags returned by the
/// <c>homeassistant/expose_entity/list</c> WebSocket command.
/// </summary>
public sealed class ExposedEntityAssistants
{
    [JsonPropertyName("conversation")]
    public bool? Conversation { get; set; }

    [JsonPropertyName("cloud.alexa")]
    public bool? CloudAlexa { get; set; }

    [JsonPropertyName("cloud.google_assistant")]
    public bool? CloudGoogleAssistant { get; set; }

    /// <summary>Returns true if exposed to at least one assistant.</summary>
    public bool IsExposedToAny =>
        Conversation == true || CloudAlexa == true || CloudGoogleAssistant == true;
}
