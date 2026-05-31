using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Reports;

public sealed class HtmlAgentData
{
    [JsonPropertyName("agentName")]
    public required string AgentName { get; init; }

    [JsonPropertyName("models")]
    public required List<HtmlModelData> Models { get; init; }
}
