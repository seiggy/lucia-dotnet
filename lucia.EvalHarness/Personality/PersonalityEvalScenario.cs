using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// A single personality evaluation scenario loaded from the personality-eval-scenarios.json data file.
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

    /// <summary>
    /// IDs of personality profiles to test this scenario against.
    /// Defaults to all profiles when omitted or empty.
    /// </summary>
    [JsonPropertyName("personalityProfileIds")]
    public List<string>? PersonalityProfileIds { get; set; }

    [JsonPropertyName("expectations")]
    public required PersonalityEvalExpectations Expectations { get; set; }
}
