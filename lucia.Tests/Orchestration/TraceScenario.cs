using System.Text.Json.Serialization;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Represents a single evaluation scenario derived from a real eval trace run.
/// Captures actual model behavior observed during eval harness execution.
/// </summary>
public sealed class TraceScenario
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("source_run")]
    public required string SourceRun { get; init; }

    [JsonPropertyName("source_model")]
    public required string SourceModel { get; init; }

    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("expected_tool")]
    public string? ExpectedTool { get; init; }

    [JsonPropertyName("expected_args")]
    public Dictionary<string, string>? ExpectedArgs { get; init; }

    [JsonPropertyName("actual_tool")]
    public string? ActualTool { get; init; }

    [JsonPropertyName("actual_args")]
    public Dictionary<string, string>? ActualArgs { get; init; }

    [JsonPropertyName("failure_type")]
    public required string FailureType { get; init; }

    [JsonPropertyName("is_regression")]
    public bool IsRegression { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("latency_ms")]
    public double LatencyMs { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Returns a display label suitable for xUnit test output.
    /// </summary>
    public override string ToString() => $"{Name} [{FailureType}]";
}
