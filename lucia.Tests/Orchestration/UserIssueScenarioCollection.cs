using System.Text.Json.Serialization;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Root container for deserialized user issue scenario JSON files.
/// </summary>
public sealed class UserIssueScenarioCollection
{
    [JsonPropertyName("metadata")]
    public TraceScenarioMetadata? Metadata { get; init; }

    [JsonPropertyName("scenarios")]
    public List<UserIssueScenario> Scenarios { get; init; } = [];
}
