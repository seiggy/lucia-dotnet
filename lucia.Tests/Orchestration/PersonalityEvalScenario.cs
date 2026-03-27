using System.Text.Json.Serialization;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Represents a single personality evaluation scenario loaded from
/// <c>TestData/personality-eval-scenarios.json</c>.
/// </summary>
public sealed class PersonalityEvalScenario
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("category")]
    public required string Category { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("skillId")]
    public required string SkillId { get; set; }

    [JsonPropertyName("action")]
    public required string Action { get; set; }

    [JsonPropertyName("agentResponse")]
    public required string AgentResponse { get; set; }

    [JsonPropertyName("personalityPrompt")]
    public required string PersonalityPrompt { get; set; }

    [JsonPropertyName("voiceTagsEnabled")]
    public bool? VoiceTagsEnabled { get; set; }

    [JsonPropertyName("expectations")]
    public required PersonalityEvalExpectations Expectations { get; set; }
}
