using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

public sealed class TemplateRenderRequest
{
    [JsonPropertyName("template")]
    public required string Template { get; init; }
}
