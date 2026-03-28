using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// A personality profile for personality-switching eval tests.
/// Loaded from the personality-profiles.json data file.
/// </summary>
public sealed class PersonalityProfile
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("instructions")]
    public required string Instructions { get; set; }

    /// <summary>
    /// Words or phrases the model SHOULD use to demonstrate it adopted the personality.
    /// At least one must appear in the rewritten response.
    /// </summary>
    [JsonPropertyName("voiceCharacteristics")]
    public List<string> VoiceCharacteristics { get; set; } = [];

    /// <summary>
    /// Phrases that must NOT appear in the rewritten response.
    /// </summary>
    [JsonPropertyName("antiPatterns")]
    public List<string> AntiPatterns { get; set; } = [];
}
