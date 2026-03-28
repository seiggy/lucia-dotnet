using System.Text.Json.Serialization;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Root container for deserialized trace scenario JSON files.
/// </summary>
public sealed class TraceScenarioCollection
{
    [JsonPropertyName("metadata")]
    public TraceScenarioMetadata? Metadata { get; init; }

    [JsonPropertyName("scenarios")]
    public List<TraceScenario> Scenarios { get; init; } = [];
}
