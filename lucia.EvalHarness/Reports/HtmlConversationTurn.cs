using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlConversationTurn
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    [JsonPropertyName("toolCalls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HtmlToolCall>? ToolCalls { get; init; }
}
