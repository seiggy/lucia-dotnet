using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Response from todo.get_items service. Key is entity_id, value is object with items array.
/// </summary>
public sealed class TodoGetItemsResponse
{
    [JsonPropertyName("items")]
    public TodoItem[] Items { get; set; } = [];
}
