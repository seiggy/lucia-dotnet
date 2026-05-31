using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlParameterData
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("topK")]
    public int TopK { get; init; }

    [JsonPropertyName("topP")]
    public double TopP { get; init; }

    [JsonPropertyName("repeatPenalty")]
    public double RepeatPenalty { get; init; }

    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; init; }
}
