using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlToolCall
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? Arguments { get; init; }
}
