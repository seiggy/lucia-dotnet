using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Tui;

internal sealed class TraceConversationToolCall
{
    [JsonPropertyName("call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? Arguments { get; init; }
}
