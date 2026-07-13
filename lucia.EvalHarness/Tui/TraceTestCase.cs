using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Tui;

internal sealed class TraceTestCase
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("score")]
    public required double? Score { get; init; }

    [JsonPropertyName("judge_status")]
    public string? JudgeStatus { get; init; }

    [JsonPropertyName("judge_reason")]
    public string? JudgeReason { get; init; }

    [JsonPropertyName("latency_ms")]
    public required double LatencyMs { get; init; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }

    [JsonPropertyName("conversation")]
    public required List<TraceConversationTurn> Conversation { get; init; }
}
