using System.Text.Json.Serialization;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Metadata header for a trace scenario collection file.
/// </summary>
public sealed class TraceScenarioMetadata
{
    [JsonPropertyName("generated_from")]
    public string? GeneratedFrom { get; init; }

    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("source_runs")]
    public List<string>? SourceRuns { get; init; }

    [JsonPropertyName("total_executions_analyzed")]
    public int TotalExecutionsAnalyzed { get; init; }

    [JsonPropertyName("models_tested")]
    public List<string>? ModelsTested { get; init; }
}
