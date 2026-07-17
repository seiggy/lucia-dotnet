using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlTestCaseData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("timedOut")]
    public bool TimedOut { get; init; }

    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("latencyMs")]
    public double LatencyMs { get; init; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; init; }

    [JsonPropertyName("conversation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HtmlConversationTurn>? Conversation { get; init; }
}
