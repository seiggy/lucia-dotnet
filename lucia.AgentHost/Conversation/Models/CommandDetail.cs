using System.Text.Json.Serialization;

namespace lucia.AgentHost.Conversation.Models;

/// <summary>
/// Details about a command that was parsed and executed via the fast-path (no LLM).
/// </summary>
public sealed record CommandDetail
{
    [JsonPropertyName("skillId")]
    public required string SkillId { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("captures")]
    public IReadOnlyDictionary<string, string>? Captures { get; init; }

    [JsonPropertyName("executionMs")]
    public long ExecutionMs { get; init; }
}
