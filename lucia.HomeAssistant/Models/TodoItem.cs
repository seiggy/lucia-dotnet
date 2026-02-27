using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Item from a Home Assistant todo list entity (todo.get_items response).
/// </summary>
public sealed class TodoItem
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "needs_action";
}
