using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Tui;

internal sealed class TraceDocument
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("agent")]
    public required string Agent { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("summary")]
    public required TraceSummary Summary { get; init; }

    [JsonPropertyName("test_cases")]
    public required List<TraceTestCase> TestCases { get; init; }
}
