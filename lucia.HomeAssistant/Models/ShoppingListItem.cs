using System.Text.Json.Serialization;

namespace lucia.HomeAssistant.Models;

/// <summary>
/// Item from the Home Assistant shopping list (GET /api/shopping_list).
/// </summary>
public sealed class ShoppingListItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("complete")]
    public bool Complete { get; set; }
}
